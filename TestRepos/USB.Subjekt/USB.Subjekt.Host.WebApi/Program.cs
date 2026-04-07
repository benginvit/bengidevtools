var builder = WebApplication.CreateBuilder(args);
builder.Services.AddOpenApi();
var app = builder.Build();
app.MapOpenApi();

app.MapGet("/health", () => new { status = "ok", service = "USB.Subjekt.Host.WebApi", time = DateTime.UtcNow })
   .WithSummary("Hälsokontroll");
app.MapGet("/api/subjects", () => new[] {
    new { Id = 1, Name = "Testsubjekt A", Status = "Active" },
    new { Id = 2, Name = "Testsubjekt B", Status = "Inactive" },
}).WithSummary("Hämta alla subjekt");
app.MapGet("/api/subjects/{id:int}", (int id) =>
    new { Id = id, Name = $"Subjekt {id}", Status = "Active" })
   .WithSummary("Hämta subjekt");
app.MapPost("/api/subjects", () =>
    Results.Created("/api/subjects/99", new { Id = 99, Name = "Nytt subjekt", Status = "Active" }))
   .WithSummary("Skapa subjekt");
app.MapPost("/api/subjects/test", () =>
    new { Id = 100, Name = "TEST-Subjekt", Status = "Active", Message = "Testsubjekt skapades" })
   .WithSummary("Skapa testsubjekt");

app.Logger.LogInformation("USB.Subjekt.Host.WebApi started");
app.Run();
