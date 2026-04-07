var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet("/health", () => new { status = "ok", service = "Support.IGUN.Host", time = DateTime.UtcNow });
app.MapGet("/", () => Results.Redirect("/health"));

app.Logger.LogInformation("Support.IGUN.Host started");

app.Run();
