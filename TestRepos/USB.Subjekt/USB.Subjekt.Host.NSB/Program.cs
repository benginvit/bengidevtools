Console.WriteLine("[info] USB.Subjekt.Host.NSB starting...");
Console.WriteLine("[info] Connecting to message bus...");

var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

try
{
    await Task.Delay(2000, cts.Token);
    Console.WriteLine("[info] Subscribed to: SubjektSkapad, SubjektAndrad");

    int tick = 0;
    while (!cts.Token.IsCancellationRequested)
    {
        await Task.Delay(5000, cts.Token);
        Console.WriteLine($"[info] Message handled #{++tick}");

        // Kastar exception efter ~25 sekunder för att testa röd-indikatorn
        if (tick == 4)
            throw new InvalidOperationException(
                "Failed to deserialize SubjektAndrad: Property 'Personnummer' not found.");
    }
}
catch (OperationCanceledException) { }
catch (Exception ex)
{
    Console.Error.WriteLine($"[crit] Unhandled exception: {ex}");
    Environment.Exit(1);
}
