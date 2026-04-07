var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet("/health", () => new { status = "ok", service = "USB.Subjekt.Host.WebApi", time = DateTime.UtcNow });
app.MapGet("/", () => Results.Redirect("/health"));

app.Logger.LogInformation("USB.Subjekt.Host.WebApi started");

app.Run();
