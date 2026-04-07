var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet("/health", () => new { status = "ok", service = "USB.Uppbord.Host.WebApi", time = DateTime.UtcNow });
app.MapGet("/", () => Results.Redirect("/health"));

app.Logger.LogInformation("USB.Uppbord.Host.WebApi started");

// Simulera periodisk aktivitet
_ = Task.Run(async () =>
{
    int tick = 0;
    while (true)
    {
        await Task.Delay(8000);
        app.Logger.LogInformation("Processing batch #{Tick}", ++tick);
    }
});

app.Run();
