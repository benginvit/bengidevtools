Console.OutputEncoding = System.Text.Encoding.UTF8;
var autoFlush = new System.IO.StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
Console.SetOut(autoFlush);

Console.WriteLine("[info] USB.Uppbord.Host.Hangfire starting...");

var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

int job = 0;
try
{
    while (!cts.Token.IsCancellationRequested)
    {
        await Task.Delay(10000, cts.Token);
        Console.WriteLine($"[info] Hangfire job #{++job} executed successfully");
    }
}
catch (OperationCanceledException) { }

Console.WriteLine("[info] USB.Uppbord.Host.Hangfire stopped");
