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

const string EVENT_COMPLETE_TRANSFER = "complete-transfer";
const string EVENT_FAIL_TRANSFER = "fail-transfer";

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.SetMinimumLevel(LogLevel.Debug);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.EnableAnnotations();
});

builder.Services.Configure<JsonOptions>(options =>
{
    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

app.UseHttpsRedirection();

app.MapPost("/create-transfer", async (CreateTransferDto request, IConfiguration config) =>
    {
        var partnerCredentials = GetPartnerCredentials(request.TenantRegion, config);

        try
        {
            var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", partnerCredentials.Credentials.PartnerServiceToken);
            httpClient.BaseAddress = new Uri(config["Transfer:PartnerCenterUrl"]);

            var TransferRequest = new
            {
                request.SourcePartnerTenantId,
                request.SourcePartnerName,
                request.CustomerEmailId,
                request.CustomerName,
                //request.TargetPartnerTenantId,
                //request.TargetPartnerEmailId,
                TransferType = "3" // 3 represents NewCommerce and should be used for Azure plan and new commerce license-based subscriptions.
            };

            var content = new StringContent(JsonSerializer.Serialize(TransferRequest), Encoding.UTF8, "application/json");

            var response = await httpClient.PostAsync($"v1/customers/{request.CustomerId}/transfers", content);

            return response.IsSuccessStatusCode
                ? Results.Ok("Request created successfully.")
                : Results.Problem(
                    detail: "Error sending data to external endpoint.",
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
    .WithMetadata(new SwaggerResponseAttribute(200, "Request created successfully"))
    .WithMetadata(new SwaggerResponseAttribute(400, "Bad request - invalid or missing data"))
    .WithMetadata(new SwaggerResponseAttribute(404, "Not found - customer or partner ID does not exist"))
    .WithMetadata(new SwaggerResponseAttribute(500, "Internal server error - unexpected error occurred"))
    .WithOpenApi();

app.MapPost("/transfer-webhook-us", async (TransferWebhookDto request, IConfiguration config) =>
    {
        try
        {
            var transfer = await GetTransfer(request.AuditUri, TenantRegion.US, config);

            await SendEmail(transfer, request.EventName, TenantRegion.US, config);

            return await SendToCumulus(transfer, config);
        }
        catch (Exception ex)
        {
            return Results.Problem($"Error: {ex.Message}");
        }
    })
    .WithName("TransferWebhookUsa")
    .WithOpenApi();

app.MapPost("/transfer-webhook-ca", async (TransferWebhookDto request, IConfiguration config) =>
{
    try
    {
        var transfer = await GetTransfer(request.AuditUri, TenantRegion.CA, config);

        await SendEmail(transfer, request.EventName, TenantRegion.CA, config);

        return await SendToCumulus(transfer, config);
    }
    catch (Exception ex)
    {
        return Results.Problem($"Error: {ex.Message}");
    }
})
    .WithName("TransferWebhookCanada")
    .WithOpenApi();


app.Run();

#region Private
async Task<Transfer> GetTransfer(string url, TenantRegion region, IConfiguration config)
{
    var partnerCredentials = GetPartnerCredentials(region, config);

    var httpClient = new HttpClient();
    httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", partnerCredentials.Credentials.PartnerServiceToken);
    httpClient.BaseAddress = new Uri(config["Transfer:PartnerCenterUrl"]);

    var parts = url.Split('_');

    var customerId = parts.GetValue(1).ToString();
    var transferId = parts.GetValue(2).ToString();

    var response = await httpClient.GetAsync($"v1/customers/{customerId}/transfers/{transferId}");

    var result = await response.Content.ReadAsStringAsync();

    return JsonSerializer.Deserialize<Transfer>(result);
}

async Task SendEmail(Transfer transfer, string status, TenantRegion region, IConfiguration config)
{
    var apiKey = config["SendGrid:ApiKey"];
    var fromEmail = config["SendGrid:FromEmail"];
    var fromName = config["SendGrid:FromName"];
    var toEmail = config["SendGrid:ToEmail"];
    var toName = config["SendGrid:ToName"];

    var client = new SendGridClient(apiKey);
    var from = new EmailAddress(fromEmail, fromName);
    var subject = $"NCE Transfer {status} - {region.ToString()}";
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
                        </ul>

                        <p>If you have any questions or need further assistance, please don’t hesitate to reach out.</p>

                        <p>Best regards,<br/>
                        <strong>Sherweb Support Team</strong>
                    </body>
                    </html>";

    var msg = MailHelper.CreateSingleEmail(from, to, subject, null, htmlContent);

    await client.SendEmailAsync(msg);
}

IAggregatePartner GetPartnerCredentials(TenantRegion region, IConfiguration config)
{
    var regionKey = region.ToString();

    var clientId = config[$"Transfer:TenantRegion:{regionKey}:ClientId"];
    var appSecret = config[$"Transfer:TenantRegion:{regionKey}:AppSecret"];
    var appDomain = config[$"Transfer:TenantRegion:{regionKey}:AppDomain"];

    if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(appSecret) || string.IsNullOrEmpty(appDomain))
    {
        throw new InvalidOperationException($"Missing configuration for region '{regionKey}'. Ensure that all credentials are provided.");
    }

    IPartnerCredentials partnerCredentials = PartnerCredentials.Instance.GenerateByApplicationCredentials(clientId, appSecret, appDomain);

    return PartnerService.Instance.CreatePartnerOperations(partnerCredentials);
}

async Task<IResult> SendToCumulus(Transfer transfer, IConfiguration configuration)
{
    var httpClient = new HttpClient();

    var jsonContent = new StringContent(JsonSerializer.Serialize(transfer), Encoding.UTF8, "application/json");

    var endpointUrl = configuration["Transfer:CumulusEndpoint"];

    var httpResponse = await httpClient.PostAsync(endpointUrl, jsonContent);

    return httpResponse.IsSuccessStatusCode
        ? Results.Ok("Notification sent successfully.")
        : Results.Problem("Error sending data to external endpoint.");
}
#endregion
