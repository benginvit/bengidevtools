var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet("/health", () => new { status = "ok", service = "Support.Host", time = DateTime.UtcNow });
app.MapGet("/", () => Results.Redirect("/health"));

app.Logger.LogInformation("Support.Host started");

app.Run();
