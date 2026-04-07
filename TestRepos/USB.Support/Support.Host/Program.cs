var builder = WebApplication.CreateBuilder(args);
builder.Services.AddOpenApi();
var app = builder.Build();
app.MapOpenApi();

app.MapGet("/health", () => new { status = "ok", service = "Support.Host", time = DateTime.UtcNow })
   .WithSummary("Hälsokontroll");
app.MapGet("/api/tickets", () => new[] {
    new { Id = 1, Title = "Testärende 1", Priority = "High",   Status = "Open"   },
    new { Id = 2, Title = "Testärende 2", Priority = "Normal", Status = "Closed" },
}).WithSummary("Hämta ärenden");
app.MapPost("/api/tickets", () =>
    Results.Created("/api/tickets/99", new { Id = 99, Title = "Nytt ärende", Status = "Open" }))
   .WithSummary("Skapa ärende");
app.MapPost("/api/tickets/test", () =>
    new { Id = 100, Title = "TEST-Ärende", Status = "Open", Message = "Testärende skapades" })
   .WithSummary("Skapa testärende");
app.MapPut("/api/tickets/{id:int}/close", (int id) =>
    Results.Ok(new { Id = id, Status = "Closed" }))
   .WithSummary("Stäng ärende");

app.Logger.LogInformation("Support.Host started");
app.Run();
