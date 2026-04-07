var builder = WebApplication.CreateBuilder(args);
builder.Services.AddOpenApi();
var app = builder.Build();
app.MapOpenApi();

app.MapGet("/health", () => new { status = "ok", service = "Support.IGUN.Host", time = DateTime.UtcNow })
   .WithSummary("Hälsokontroll");
app.MapGet("/api/notifications", () => new[] {
    new { Id = 1, Message = "Testnotis 1", Channel = "Email", Sent = false },
    new { Id = 2, Message = "Testnotis 2", Channel = "SMS",   Sent = true  },
}).WithSummary("Hämta notifikationer");
app.MapPost("/api/notifications/send", () =>
    new { Sent = 3, Failed = 0, Message = "Notifikationer skickade" })
   .WithSummary("Skicka notifikationer");
app.MapPost("/api/notifications/test", () =>
    new { Id = 100, Message = "TEST-Notis", Channel = "Email", Sent = false })
   .WithSummary("Skapa testnotis");

app.Logger.LogInformation("Support.IGUN.Host started");
app.Run();
