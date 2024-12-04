using SendGrid.Helpers.Mail;
using SendGrid;
using System.Text;
using Swashbuckle.AspNetCore.Annotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Store.PartnerCenter;
using Microsoft.Store.PartnerCenter.Extensions;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Xtkl.NceTransferWebhooks.DTOs;
using Xtkl.NceTransferWebhooks.Model;
using Microsoft.Extensions.Caching.Memory;
using Xtkl.Apps.Legacy.Services.Client.Inspectors;
using Xtkl.Apps.Legacy.Services.Client;
using Xtkl.Apps.Legacy.Services.Contracts.AdminPortal;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.SetMinimumLevel(LogLevel.Debug);

builder.Services.AddApplicationInsightsTelemetry();
builder.Logging.AddApplicationInsights();

builder.Services.AddMemoryCache();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.EnableAnnotations();
});

builder.Services.Configure<JsonOptions>(options =>
{
    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

var serviceConfig = builder.Configuration.GetSection("Transfer:ImportService");

builder.Services.AddScoped<IAdminPortalFacade>(provider =>
{
    var identityResolver = provider.GetService<IIdentityResolver>();
    var correlationResolver = provider.GetService<ICorrelationResolver>();

    var baseUrl = serviceConfig["Url"];
    var username = serviceConfig["Username"];
    var password = serviceConfig["Password"];

    return LegacyServicesFactory.GetAdminPortalFacadeChannel(
        baseUrl,
        username,
        password,
        identityResolver,
        correlationResolver
    );
});

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

app.UseHttpsRedirection();

app.MapPost("/create-transfer", async (CreateTransferDto request, IConfiguration config, IMemoryCache memoryCache, ILogger<Program> logger) =>
{
    if (request.TenantId == Guid.Empty || request.PartnerId == Guid.Empty || string.IsNullOrEmpty(request.CumulusOrganizationUniqueName) ||
        string.IsNullOrEmpty(request.CustomerEmail) || string.IsNullOrEmpty(request.PartnerName) ||
        string.IsNullOrEmpty(request.CustomerName))
    {
        return Results.Ok("'TenantId', 'PartnerId', 'PartnerName', 'CustomerName', 'CustomerEmailId', 'CumulusOrganizationUniqueName' are required.");
    }

    try
    {
        var partnerCredentials = await GetPartnerCredentials(request.TenantRegion, config, memoryCache);

        var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", partnerCredentials.PartnerServiceToken);
        httpClient.BaseAddress = new Uri(config["Transfer:PartnerCenterUrl"]);

        var TransferRequest = new
        {
            SourcePartnerTenantId = request.PartnerId,
            SourcePartnerName = request.PartnerName,
            CustomerEmailId = request.CustomerEmail,
            request.CustomerName,
            TargetPartnerEmailId = request.CumulusOrganizationUniqueName,// hack
            TransferType = TransferType.NewCommerce.GetHashCode()
        };

        var content = new StringContent(JsonSerializer.Serialize(TransferRequest), Encoding.UTF8, "application/json");

        var response = await httpClient.PostAsync($"v1/customers/{request.TenantId}/transfers", content);

        if (!response.IsSuccessStatusCode)
        {
            logger.LogError($"Method: create-transfer -- Customer Id: {request.TenantId} -- Cumulus Unique name: {request.CumulusOrganizationUniqueName} -- Partner Id: {request.PartnerId} -- Error: Not Created", request);

            return Results.Problem(
                detail: "Internal server error - unexpected error occurred",
                statusCode: (int)response.StatusCode
            );
        }

        var result = await response.Content.ReadAsStringAsync();
        var transfer = JsonSerializer.Deserialize<Transfer>(result);

        logger.LogWarning($"Method: create-transfer -- Transfer Id: {transfer.id} -- Customer Id: {transfer.customerTenantId} -- Cumulus Org Id: {transfer.targetPartnerEmailId} -- Message: Success");
        return Results.Ok(new { TransferID = transfer.id, CustomerID = transfer.customerTenantId, CustomerName = transfer.customerName });
    }
    catch (Exception ex)
    {
        logger.LogError($"Method: create-transfer -- Partner Id: {request.PartnerId} -- Customer Id: {request.TenantId} -- Cumulus Org Id: {request.CumulusOrganizationUniqueName} -- Error: {ex}");
        return Results.Problem("Internal server error - unexpected error occurred");
    }
})
.WithName("CreateTransfer")
.WithMetadata(new SwaggerOperationAttribute(
        summary: "Creates a new NCE transfer request",
        description: "This endpoint creates a transfer request for a customer's subscription between partners."
    ))
    .WithMetadata(new SwaggerResponseAttribute(200, "Transfer created successfully"))
    .WithMetadata(new SwaggerResponseAttribute(400, "'TenantId', 'PartnerId', 'PartnerName', 'CustomerName', 'CustomerEmailId', and 'CumulusOrganizationUniqueName' are required"))
    .WithMetadata(new SwaggerResponseAttribute(500, "Internal server error - unexpected error occurred"))
    .WithOpenApi();

app.MapPost("/transfer-webhook-us", async (TransferWebhookDto request, IAdminPortalFacade adminFacade, IConfiguration config, IMemoryCache memoryCache, ILogger<Program> logger) =>
{
    var transfer = await GetTransfer(request.AuditUri, TenantRegion.US, config, memoryCache);

    try
    {
        logger.LogWarning($"US Transfer: {transfer}");

        ImportTransferToCumulus(request.EventName, transfer, adminFacade, logger);

        return await SendToFrontdesk(transfer, request.EventName, config, logger);
    }
    catch (Exception ex)
    {
        logger.LogError($"Method: transfer-webhook-us -- Transfer Id: {transfer.id} -- Customer Id: {transfer.customerTenantId} -- Cumulus Org Id: {transfer.targetPartnerEmailId} -- Error: {ex}");
        return Results.Problem($"Error: {ex.Message}");
    }
})
.WithName("TransferWebhookUsa")
.WithMetadata(new SwaggerOperationAttribute(
summary: "This event is raised when the transfer is complete",
description: "This endpoint receives notifications from the Microsoft Partner Center when an NCE (New Commerce Experience) transfer is completed or expires within the US tenant environment."
))
.WithMetadata(new SwaggerResponseAttribute(200, "Notification processed successfully"))
    .WithMetadata(new SwaggerResponseAttribute(409, "The transfer cannot be processed because its status is neither 'Complete' nor 'Expired,' or the action is not an incoming transfer."))
    .WithMetadata(new SwaggerResponseAttribute(500, "Internal server error - unexpected error occurred"))
    .WithOpenApi();

app.MapPost("/transfer-webhook-ca", async (TransferWebhookDto request, IAdminPortalFacade adminFacade, IConfiguration config, IMemoryCache memoryCache, ILogger<Program> logger) =>
{
    var transfer = await GetTransfer(request.AuditUri, TenantRegion.CA, config, memoryCache);

    try
    {
        logger.LogWarning($"CA Transfer: {transfer}");

        ImportTransferToCumulus(request.EventName, transfer, adminFacade, logger);

        return await SendToFrontdesk(transfer, request.EventName, config, logger);
    }
    catch (Exception ex)
    {
        logger.LogError($"Method: transfer-webhook-ca -- Transfer Id: {transfer.id} -- Customer Id: {transfer.customerTenantId} -- Cumulus Org Id: {transfer.targetPartnerEmailId} -- Error: {ex}");
        return Results.Problem($"Error: {ex.Message}");
    }
})
    .WithName("TransferWebhookCanada")
    .WithMetadata(new SwaggerOperationAttribute(
        summary: "This event is raised when the transfer is complete",
        description: "This endpoint receives notifications from the Microsoft Partner Center when an NCE (New Commerce Experience) transfer is completed or expires within the CA tenant environment."
    ))
    .WithMetadata(new SwaggerResponseAttribute(200, "Notification processed successfully"))
    .WithMetadata(new SwaggerResponseAttribute(409, "The transfer cannot be processed because its status is neither 'Complete' nor 'Expired,' or the action is not an incoming transfer."))
    .WithMetadata(new SwaggerResponseAttribute(500, "Internal server error - unexpected error occurred"))
    .WithOpenApi();

app.MapPost("/transfer-webhook-eu", async (TransferWebhookDto request, IAdminPortalFacade adminFacade, IConfiguration config, IMemoryCache memoryCache, ILogger<Program> logger) =>
{
    var transfer = await GetTransfer(request.AuditUri, TenantRegion.EU, config, memoryCache);

    try
    {
        logger.LogWarning($"EU Transfer: {transfer}");

        ImportTransferToCumulus(request.EventName, transfer, adminFacade, logger);

        return await SendToFrontdesk(transfer, request.EventName, config, logger);
    }
    catch (Exception ex)
    {
        logger.LogError($"Method: transfer-webhook-eu -- Transfer Id: {transfer.id} -- Customer Id: {transfer.customerTenantId} -- Cumulus Org Id: {transfer.targetPartnerEmailId} -- Error: {ex}");
        return Results.Problem($"Error: {ex.Message}");
    }
})
    .WithName("TransferWebhookEuropa")
    .WithMetadata(new SwaggerOperationAttribute(
        summary: "This event is raised when the transfer is complete",
        description: "This endpoint receives notifications from the Microsoft Partner Center when an NCE (New Commerce Experience) transfer is completed or expires within the EU tenant environment."
    ))
    .WithMetadata(new SwaggerResponseAttribute(200, "Notification processed successfully"))
    .WithMetadata(new SwaggerResponseAttribute(409, "The transfer cannot be processed because its status is neither 'Complete' nor 'Expired,' or the action is not an incoming transfer."))
    .WithMetadata(new SwaggerResponseAttribute(500, "Internal server error - unexpected error occurred"))
    .WithOpenApi();


app.Run();

#region Private
async Task<Transfer> GetTransfer(string url, TenantRegion region, IConfiguration config, IMemoryCache memoryCache)
{
    var partnerCredentials = await GetPartnerCredentials(region, config, memoryCache);

    var httpClient = new HttpClient();
    httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", partnerCredentials.PartnerServiceToken);
    httpClient.BaseAddress = new Uri(config["Transfer:PartnerCenterUrl"]);

    var parts = url.Split('_');

    var customerId = parts.GetValue(1).ToString();
    var transferId = parts.GetValue(2).ToString();

    var response = await httpClient.GetAsync($"v1/customers/{customerId}/transfers/{transferId}");

    var result = await response.Content.ReadAsStringAsync();

    return JsonSerializer.Deserialize<Transfer>(result);
}
async Task SendEmail(Transfer transfer, string eventName, string imported, TenantRegion region, IConfiguration config)
{
    var apiKey = config["SendGrid:ApiKey"];
    var fromEmail = config["SendGrid:FromEmail"];
    var fromName = config["SendGrid:FromName"];
    var toEmail = config["SendGrid:ToEmail"];
    var toName = config["SendGrid:ToName"];

    var client = new SendGridClient(apiKey);
    var from = new EmailAddress(fromEmail, fromName);
    var subject = $"NCE Transfer {eventName} - {region.ToString()}";
    var to = new EmailAddress(toEmail, toName);

    var htmlContent = $@"
                    <html>
                    <body>
                        <p>Team,</p>
                        <p>We would like to inform you that the NCE transfer process has been successfully completed. Please find the details below:</p>

                        <ul style='list-style-type:none; padding: 0;'>
                            <li><strong>Transfer ID:</strong> {transfer.id}</li>
                            <li><strong>Customer ID:</strong> {transfer.customerTenantId}</li>
                            <li><strong>Customer Name:</strong> {transfer.customerName}</li>
                            <li><strong>Source Partner Name:</strong> {transfer.sourcePartnerName}</li>
                            <li><strong>Target Partner Name:</strong> {transfer.targetPartnerName}</li>
                            <li><strong>Created Time:</strong> {transfer.createdTime}</li>
                            <li><strong>Complete Time:</strong> {transfer.completedTime}</li>
                            <li><strong>Expired Time:</strong> {transfer.expirationTime}</li>
                            <li><strong>Status:</strong> {transfer.status}</li>
                            <li><strong>Direction:</strong> {transfer.transferDirection}</li>
                            <li><strong>Was imported:</strong> {imported}</li>
                        </ul>

                        <p>If you have any questions or need further assistance, please don’t hesitate to reach out.</p>

                        <p>Best regards,<br/>
                        <strong>Sherweb Support Team</strong>
                    </body>
                    </html>";

    var msg = MailHelper.CreateSingleEmail(from, to, subject, null, htmlContent);

    await client.SendEmailAsync(msg);
}
async Task<IResult> SendToFrontdesk(Transfer transfer, string transferEventName, IConfiguration configuration, ILogger<Program> logger)
{
    if (transfer.transferDirection != (int)TransferDirection.IncomingTransfer)
    {
        return Results.Conflict("The transfer cannot be processed because it is not an incoming transfer.");
    }

    if (!transfer.status.Equals(TransferStatus.Complete.ToString(), StringComparison.OrdinalIgnoreCase) &&
        !transfer.status.Equals(TransferStatus.Expired.ToString(), StringComparison.OrdinalIgnoreCase))
    {
        return Results.Conflict("The transfer cannot be processed because its status is neither 'Complete' nor 'Expired.");
    }

    if (transfer.status.Equals(TransferStatus.Complete.ToString(), StringComparison.OrdinalIgnoreCase) &&
        !transferEventName.Equals(TransferEventType.CompleteTransfer.ToTransferEventString(), StringComparison.OrdinalIgnoreCase))
    {
        return Results.Conflict("The transfer cannot be processed because in spite of its status is 'Complete', the transfer event is not completed yet.");
    }

    var httpClient = new HttpClient();

    var jsonContent = new StringContent(JsonSerializer.Serialize(transfer), Encoding.UTF8, "application/json");

    var endpointUrl = configuration["Transfer:FrontdeskEndpoint"];

    var httpResponse = await httpClient.PostAsync(endpointUrl, jsonContent);

    if (httpResponse.IsSuccessStatusCode)
    {
        logger.LogWarning($"Method: SendToFrontdesk -- Transfer Id: {transfer.id} -- Customer Id: {transfer.customerTenantId} -- Cumulus Org Id: {transfer.targetPartnerEmailId} -- Message: Success");
        Results.Ok("Notification processed successfully");
    }

    logger.LogError($"Method: SendToFrontdesk -- Transfer Id: {transfer.id} -- Customer Id: {transfer.customerTenantId} -- Cumulus Org Id: {transfer.targetPartnerEmailId} -- Message: Not sent to Cumulus");
    return Results.Problem("Internal server error - unexpected error occurred");
}
async Task<IPartnerCredentials> GetPartnerCredentials(TenantRegion region, IConfiguration config, IMemoryCache memoryCache)
{
    var regionKey = region.ToString();

    var clientId = config[$"Transfer:TenantRegion:{regionKey}:ClientId"];
    var appSecret = config[$"Transfer:TenantRegion:{regionKey}:AppSecret"];
    var tenantId = config[$"Transfer:TenantRegion:{regionKey}:TenantId"];
    var refreshToken = config[$"Transfer:TenantRegion:{regionKey}:RefreshToken"];

    var loginUrl = config[$"Transfer:ADDLoginUrl"];
    var scope = config["Transfer:Scope"];

    if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(appSecret) || string.IsNullOrEmpty(tenantId) || string.IsNullOrEmpty(refreshToken))
    {
        throw new InvalidOperationException($"Missing configuration for region '{regionKey}'. Ensure that all credentials are provided.");
    }

    var cacheKey = $"AuthToken_{regionKey}";

    if (memoryCache.TryGetValue<AuthenticationToken>(cacheKey, out var cachedAuthToken) &&
        cachedAuthToken.ExpiryTime > DateTimeOffset.UtcNow)
    {
        return await PartnerCredentials.Instance.GenerateByUserCredentialsAsync(clientId, cachedAuthToken);
    }

    var postData = new Dictionary<string, string>
    {
        { "client_id", clientId },
        { "scope", scope },
        { "refresh_token", refreshToken },
        { "grant_type", "refresh_token" },
        { "client_secret", appSecret }
    };

    var url = $"{loginUrl}/{tenantId}/oauth2/token";
    TokenResponse tokenResponse = null;

    using (var client = new HttpClient())
    {
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/x-www-form-urlencoded"));

        var content = new FormUrlEncodedContent(postData);
        var response = await client.PostAsync(url, content);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException("Error authenticating user.");
        }

        var responseContent = await response.Content.ReadAsStringAsync();
        tokenResponse = JsonSerializer.Deserialize<TokenResponse>(responseContent)
                        ?? throw new InvalidOperationException("Failed to deserialize token response.");
    }

    var authToken = new AuthenticationToken(
        tokenResponse.access_token,
        DateTimeOffset.FromUnixTimeSeconds(Convert.ToInt64(tokenResponse.expires_on))
    );

    memoryCache.Set(cacheKey, authToken);

    return await PartnerCredentials.Instance.GenerateByUserCredentialsAsync(clientId, authToken);
}
void ImportTransferToCumulus(string transferEventName, Transfer transfer, IAdminPortalFacade adminFacade, ILogger<Program> logger)
{
    try
    {
        if (transfer.transferDirection == (int)TransferDirection.IncomingTransfer &&
            transferEventName.Equals(TransferEventType.CompleteTransfer.ToTransferEventString(), StringComparison.OrdinalIgnoreCase) &&
            transfer.status.Equals(TransferStatus.Complete.ToString(), StringComparison.OrdinalIgnoreCase))
        {

            var transferResult = adminFacade.ImportMicrosoftTransferInUsingUniqueId(transfer.targetPartnerEmailId);//OrgUnique

            if (!transferResult.IsSuccess)
            {
                logger.LogError($"Method: ImportTransferToCumulus -- Transfer Id: {transfer.id} -- Customer Id: {transfer.customerTenantId} -- Cumulus Org Id: {transfer.targetPartnerEmailId} -- Error: {transferResult.Error}");
                return;
            }

            logger.LogWarning($"Method: ImportTransferToCumulus -- Transfer Id: {transfer.id} -- Customer Id: {transfer.customerTenantId} -- Cumulus Org Id: {transfer.targetPartnerEmailId} -- Message: Success");

            return;
        }
    }
    catch (Exception ex)
    {
        logger.LogError($"Method: ImportTransferToCumulus -- Transfer Id: {transfer.id} -- Customer Id: {transfer.customerTenantId} -- Cumulus Org Id: {transfer.targetPartnerEmailId} -- Message: {ex.Message}");
    }
   
    logger.LogWarning($"Method: ImportTransferToCumulus -- Transfer Id: {transfer.id} -- Customer Id: {transfer.customerTenantId} -- Cumulus Org Id: {transfer.targetPartnerEmailId} -- Message: Not to be imported");
}
#endregion

