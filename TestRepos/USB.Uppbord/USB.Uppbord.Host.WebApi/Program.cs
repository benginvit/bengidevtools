var builder = WebApplication.CreateBuilder(args);
builder.Services.AddOpenApi();
var app = builder.Build();
app.MapOpenApi();

app.MapGet("/health", () => new { status = "ok", service = "USB.Uppbord.Host.WebApi", time = DateTime.UtcNow })
   .WithSummary("Hälsokontroll");

app.MapGet("/api/orders", () => new[] {
    new { Id = 1, OrderNumber = "ORD-001", Status = "New",       CustomerId = 1 },
    new { Id = 2, OrderNumber = "ORD-002", Status = "Completed", CustomerId = 2 },
}).WithSummary("Hämta alla ordrar");

app.MapGet("/api/orders/{id:int}", (int id) =>
    new { Id = id, OrderNumber = $"ORD-00{id}", Status = "New" })
   .WithSummary("Hämta order");

app.MapPost("/api/orders", (CreateOrderRequest req) =>
    Results.Created("/api/orders/99", new { Id = 99, req.CustomerId, req.ArticleNumber, Status = "New" }))
   .WithSummary("Skapa order");

app.MapPost("/api/orders/test", (CreateOrderRequest req) =>
    new { Id = 100, req.CustomerId, req.ArticleNumber, Status = "New", Message = "Testorder skapad" })
   .WithSummary("Skapa testorder");

app.MapDelete("/api/orders/{id:int}", (int id) => Results.Ok(new { Deleted = id }))
   .WithSummary("Ta bort order");

app.Logger.LogInformation("USB.Uppbord.Host.WebApi started");
_ = Task.Run(async () => {
    int tick = 0;
    while (true) { await Task.Delay(8000); app.Logger.LogInformation("Processing batch #{Tick}", ++tick); }
});
app.Run();

record CreateOrderRequest(int CustomerId, string ArticleNumber, int Quantity, decimal UnitPrice);
