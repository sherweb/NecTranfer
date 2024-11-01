using System.Net.Mail;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();


app.MapPost("/transfer-completed", async (CompleteTransferDto request) =>
    {
        try
        {
            //var smtpClient = new SmtpClient("smtp.gmail.com")
            //{
            //    Port = 587,
            //    Credentials = new System.Net.NetworkCredential("alessandro.developer.santos@gmail.com", "ylmsnhumxxnumurn"),
            //    EnableSsl = true,
            //};

            //var mailMessage = new MailMessage
            //{
            //    From = new MailAddress("alessandro.developer.santos@gmail.com", "Sherweb"),
            //    Subject = "NCE Transfer Completed",
            //    IsBodyHtml = true,
            //    Body = $@"
            //    <html>
            //    <body>
            //        <p>Team,</p>
            //        <p>We would like to inform you that the NCE transfer process has been successfully completed. Please find the details below:</p>
                    
            //        <ul style='list-style-type:none; padding: 0;'>
            //            <li><strong>Resource URI:</strong> {request.ResourceUri}</li>
            //            <li><strong>Date of Change (UTC):</strong> {request.ResourceChangeUtcDate.ToString("u")}</li>
            //        </ul>
                    
            //        <p>If you have any questions or need further assistance, please don’t hesitate to reach out.</p>
                    
            //        <p>Best regards,<br/>
            //        <strong>Sherweb Support Team</strong>
            //    </body>
            //    </html>"
            //};

            //mailMessage.To.Add("asantos@sherweb.com");

            //await smtpClient.SendMailAsync(mailMessage);
            return Results.Ok("Request received.");
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
