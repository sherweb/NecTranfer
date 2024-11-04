using SendGrid;
using SendGrid.Helpers.Mail;

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

app.MapPost("/transfer-completed", async (CompleteTransferDto request, IConfiguration config) =>
    {
        var apiKey = config["SendGrid:ApiKey"];
        var fromEmail = config["SendGrid:FromEmail"];
        var fromName = config["SendGrid:FromName"];
        var toEmail = config["SendGrid:ToEmail"];
        var toName = config["SendGrid:ToName"];

        var client = new SendGridClient(apiKey);
        var from = new EmailAddress(fromEmail, fromName);
        var subject = "NCE Transfer Completed";
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

        try
        {
            var response = await client.SendEmailAsync(msg);

            return response.IsSuccessStatusCode ? Results.Ok("Email sent successfully.") : Results.Problem("Error sending email.");
        }
        catch (Exception ex)
        {
            return Results.Problem($"Error sending email: {ex.Message}");
        }
    })
    .WithName("TransferCompleted")
    .WithOpenApi();

app.Run();

internal record CompleteTransferDto(string EventName, string ResourceUri, string ResourceName, string AuditUri, DateTime ResourceChangeUtcDate);
