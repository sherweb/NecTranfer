using SendGrid.Helpers.Mail;
using SendGrid;
using System.Text;
using JsonSerializer = System.Text.Json.JsonSerializer;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.SetMinimumLevel(LogLevel.Debug);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

app.UseHttpsRedirection();

app.MapPost("/transfer-completed-us", async (CompleteTransferDto request, IConfiguration config) =>
    {
        var apiKey = config["SendGrid:ApiKey"];
        var fromEmail = config["SendGrid:FromEmail"];
        var fromName = config["SendGrid:FromName"];
        var toEmail = config["SendGrid:ToEmail"];
        var toName = config["SendGrid:ToName"];

        try
        {
            var client = new SendGridClient(apiKey);
            var from = new EmailAddress(fromEmail, fromName);
            var subject = "NCE Transfer Completed - US";
            var to = new EmailAddress(toEmail, toName);

            var htmlContent = $@"
                <html>
                <body>
                    <p>Team,</p>
                    <p>We would like to inform you that the NCE transfer process has been successfully completed. Please find the details below:</p>

                    <p>{JsonSerializer.Serialize(request)}</p>

                    <ul style='list-style-type:none; padding: 0;'>
                        <li><strong>Resource URI:</strong> {request.ResourceUri}</li>
                        <li><strong>Date of Change (UTC):</strong> {request.ResourceChangeUtcDate.ToString("u")}</li>
                    </ul>

                    <p>If you have any questions or need further assistance, please don’t hesitate to reach out.</p>

                    <p>Best regards,<br/>
                    <strong>Sherweb Support Team</strong>
                </body>
                </html>";

            var msg = MailHelper.CreateSingleEmail(from, to, subject, null, htmlContent);

            await client.SendEmailAsync(msg);

            var httpClient = new HttpClient();

            var jsonContent = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");

            var endpointUrl = config["Transfer:CumulusEndpoint"];

            var httpResponse = await httpClient.PostAsync(endpointUrl, jsonContent);

            return httpResponse.IsSuccessStatusCode
                    ? Results.Ok("Notification sent successfully.")
                    : Results.Problem("Error sending data to external endpoint.");
        }
        catch (Exception ex)
        {
            return Results.Problem($"Error sending email: {ex.Message}");
        }
    })
    .WithName("TransferCompletedUsa")
    .WithOpenApi();

app.MapPost("/transfer-completed-ca", async (CompleteTransferDto request, IConfiguration config) =>
{
    var apiKey = config["SendGrid:ApiKey"];
    var fromEmail = config["SendGrid:FromEmail"];
    var fromName = config["SendGrid:FromName"];
    var toEmail = config["SendGrid:ToEmail"];
    var toName = config["SendGrid:ToName"];

    try
    {
        var client = new SendGridClient(apiKey);
        var from = new EmailAddress(fromEmail, fromName);
        var subject = "NCE Transfer Completed - CA";
        var to = new EmailAddress(toEmail, toName);

        var htmlContent = $@"
                <html>
                <body>
                    <p>Team,</p>
                    <p>We would like to inform you that the NCE transfer process has been successfully completed. Please find the details below:</p>

                    <ul style='list-style-type:none; padding: 0;'>
                        <li><strong>Resource URI:</strong> {request.ResourceUri}</li>
                        <li><strong>Date of Change (UTC):</strong> {request.ResourceChangeUtcDate.ToString("u")}</li>
                    </ul>

                    <p>If you have any questions or need further assistance, please don’t hesitate to reach out.</p>

                    <p>Best regards,<br/>
                    <strong>Sherweb Support Team</strong>
                </body>
                </html>";

        var msg = MailHelper.CreateSingleEmail(from, to, subject, null, htmlContent);

        await client.SendEmailAsync(msg);

        var httpClient = new HttpClient();

        var jsonContent = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");

        var endpointUrl = config["Transfer:CumulusEndpoint"];

        var httpResponse = await httpClient.PostAsync(endpointUrl, jsonContent);

        return httpResponse.IsSuccessStatusCode
                ? Results.Ok("Notification sent successfully.")
                : Results.Problem("Error sending data to external endpoint.");
    }
    catch (Exception ex)
    {
        return Results.Problem($"Error sending email: {ex.Message}");
    }
})
    .WithName("TransferCompletedCanada")
    .WithOpenApi();

app.Run();

internal record CompleteTransferDto(string EventName, string ResourceUri, string ResourceName, string AuditUri, DateTime ResourceChangeUtcDate);
