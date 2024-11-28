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
//using Xtkl.Apps.Legacy.Services.Client.Inspectors;
//using Xtkl.Apps.Legacy.Services.Client;
//using Xtkl.Apps.Legacy.Services.Contracts.AdminPortal;


var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.SetMinimumLevel(LogLevel.Debug);

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

//var serviceConfig = builder.Configuration.GetSection("Transfer:ImportService");

//builder.Services.AddScoped<IAdminPortalFacade>(provider =>
//{
//    var identityResolver = provider.GetService<IIdentityResolver>();
//    var correlationResolver = provider.GetService<ICorrelationResolver>();

//    var baseUrl = serviceConfig["Url"];
//    var username = serviceConfig["Username"];
//    var password = serviceConfig["Password"];

//    return LegacyServicesFactory.GetAdminPortalFacadeChannel(
//        baseUrl,
//        username,
//        password,
//        identityResolver,
//        correlationResolver
//    );
//});

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

app.UseHttpsRedirection();

app.MapPost("/create-transfer", async (CreateTransferDto request, IConfiguration config, IMemoryCache memoryCache) =>
    {
        if (request.TenantId == Guid.Empty || request.PartnerId == Guid.Empty || request.CumulusOrgId == Guid.Empty ||
            string.IsNullOrEmpty(request.CustomerEmail) || string.IsNullOrEmpty(request.PartnerName) ||
            string.IsNullOrEmpty(request.CustomerName))
        {
            return Results.Ok("'TenantId', 'PartnerId', 'PartnerName', 'CustomerName', 'CustomerEmailId', 'CumulusOrgId' are required.");
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
                TargetPartnerEmailId = request.CumulusOrgId,
                TransferType = TransferType.NewCommerce.GetHashCode()
            };

            var content = new StringContent(JsonSerializer.Serialize(TransferRequest), Encoding.UTF8, "application/json");

            var response = await httpClient.PostAsync($"v1/customers/{request.TenantId}/transfers", content);

            return response.IsSuccessStatusCode
                ? Results.Ok("Transfer created successfully.")
                : Results.Problem(
                    detail: "Internal server error - unexpected error occurred",
                    statusCode: (int)response.StatusCode
                );
        }
        catch (Exception ex)
        {
            return Results.Problem("Internal server error - unexpected error occurred");
        }
    })
    .WithName("CreateTransfer")
    .WithMetadata(new SwaggerOperationAttribute(
        summary: "Creates a new NCE transfer request",
        description: "This endpoint creates a transfer request for a customer's subscription between partners."
    ))
    .WithMetadata(new SwaggerResponseAttribute(200, "Transfer created successfully"))
    .WithMetadata(new SwaggerResponseAttribute(400, "'TenantId', 'PartnerId', 'PartnerName', 'CustomerName', 'CustomerEmailId', and 'CumulusOrgId' are required"))
    .WithMetadata(new SwaggerResponseAttribute(500, "Internal server error - unexpected error occurred"))
    .WithOpenApi();

app.MapPost("/transfer-webhook-us", async (TransferWebhookDto request, IConfiguration config, IMemoryCache memoryCache) =>
    {
        try
        {
            var transfer = await GetTransfer(request.AuditUri, TenantRegion.US, config, memoryCache);

            // TO DO: Refactor it after tests
            var wasImported = "No";
            //if (transfer.status.Equals(TransferStatus.Complete.ToString(), StringComparison.OrdinalIgnoreCase))
            //{
            //    var transferResult = adminFacade.ImportMicrosoftTransferInUsingUniqueId(transfer.targetPartnerEmailId);
            //    wasImported = transferResult.IsSuccess ? "Yes" : "No";
            //}

            await SendEmail(transfer, request.EventName, wasImported, TenantRegion.US, config);

            return await SendToCumulus(transfer, config);
        }
        catch (Exception ex)
        {
            return Results.Problem($"Error: {ex.Message}");
        }
    })
    .WithName("TransferWebhookUsa")
    .WithMetadata(new SwaggerOperationAttribute(
        summary: "This event is raised when the transfer is complete",
        description: "This endpoint receives notifications from the Microsoft Partner Center when an NCE (New Commerce Experience) transfer is completed or expires within the US tenant environment."
    ))
    .WithMetadata(new SwaggerResponseAttribute(200, "Notification processed successfully"))
    .WithMetadata(new SwaggerResponseAttribute(409, "The transfer is not in 'Complete' or 'Expired' status and cannot be processed."))
    .WithMetadata(new SwaggerResponseAttribute(500, "Internal server error - unexpected error occurred"))
    .WithOpenApi();

app.MapPost("/transfer-webhook-ca", async (TransferWebhookDto request, IConfiguration config, IMemoryCache memoryCache) =>
{
    try
    {
        var transfer = await GetTransfer(request.AuditUri, TenantRegion.CA, config, memoryCache);

        // TO DO: Refactor it after tests
        var wasImported = "No";
        //if (transfer.status.Equals(TransferStatus.Complete.ToString(), StringComparison.OrdinalIgnoreCase))
        //{
        //    var transferResult = adminFacade.ImportMicrosoftTransferInUsingUniqueId(transfer.targetPartnerEmailId);
        //    wasImported = transferResult.IsSuccess ? "Yes" : "No";
        //}

        await SendEmail(transfer, request.EventName, wasImported, TenantRegion.CA, config);

        return await SendToCumulus(transfer, config);
    }
    catch (Exception ex)
    {
        return Results.Problem($"Error: {ex.Message}");
    }
})
    .WithName("TransferWebhookCanada")
    .WithMetadata(new SwaggerOperationAttribute(
        summary: "This event is raised when the transfer is complete",
        description: "This endpoint receives notifications from the Microsoft Partner Center when an NCE (New Commerce Experience) transfer is completed or expires within the CA tenant environment."
    ))
    .WithMetadata(new SwaggerResponseAttribute(200, "Notification processed successfully"))
    .WithMetadata(new SwaggerResponseAttribute(409, "The transfer is not in 'Complete' or 'Expired' status and cannot be processed."))
    .WithMetadata(new SwaggerResponseAttribute(500, "Internal server error - unexpected error occurred"))
    .WithOpenApi();

app.MapPost("/transfer-webhook-eu", async (TransferWebhookDto request, IConfiguration config, IMemoryCache memoryCache) =>
    {
        try
        {
            var transfer = await GetTransfer(request.AuditUri, TenantRegion.EU, config, memoryCache);

            // TO DO: Refactor it after tests
            var wasImported = "No";
            //if (transfer.status.Equals(TransferStatus.Complete.ToString(), StringComparison.OrdinalIgnoreCase))
            //{
            //    var transferResult = adminFacade.ImportMicrosoftTransferInUsingUniqueId(transfer.targetPartnerEmailId);
            //    wasImported = transferResult.IsSuccess ? "Yes" : "No";
            //}

            await SendEmail(transfer, request.EventName, wasImported, TenantRegion.EU, config);

            return await SendToCumulus(transfer, config);
        }
        catch (Exception ex)
        {
            return Results.Problem($"Error: {ex.Message}");
        }
    })
    .WithName("TransferWebhookEuropa")
    .WithMetadata(new SwaggerOperationAttribute(
        summary: "This event is raised when the transfer is complete",
        description: "This endpoint receives notifications from the Microsoft Partner Center when an NCE (New Commerce Experience) transfer is completed or expires within the EU tenant environment."
    ))
    .WithMetadata(new SwaggerResponseAttribute(200, "Notification processed successfully"))
    .WithMetadata(new SwaggerResponseAttribute(409, "The transfer is not in 'Complete' or 'Expired' status and cannot be processed."))
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
async Task<IResult> SendToCumulus(Transfer transfer, IConfiguration configuration)
{
    if (transfer.transferDirection != (int)TransferDirection.IncomingTransfer ||
        (!transfer.status.Equals(TransferStatus.Complete.ToString(), StringComparison.OrdinalIgnoreCase) &&
        !transfer.status.Equals(TransferStatus.Expired.ToString(), StringComparison.OrdinalIgnoreCase)))
    {
        return Results.Conflict("The transfer is not in 'Complete' or 'Expired' status and cannot be processed.");
    }

    var httpClient = new HttpClient();

    var jsonContent = new StringContent(JsonSerializer.Serialize(transfer), Encoding.UTF8, "application/json");

    var endpointUrl = configuration["Transfer:CumulusEndpoint"];

    var httpResponse = await httpClient.PostAsync(endpointUrl, jsonContent);

    return httpResponse.IsSuccessStatusCode
        ? Results.Ok("Notification processed successfully")
        : Results.Problem("Internal server error - unexpected error occurred");
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
#endregion

