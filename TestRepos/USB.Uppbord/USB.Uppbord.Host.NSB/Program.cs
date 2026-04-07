Console.OutputEncoding = System.Text.Encoding.UTF8;
var autoFlush = new System.IO.StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
Console.SetOut(autoFlush);

Console.WriteLine("[info] USB.Uppbord.Host.NSB starting...");

var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

int tick = 0;
try
{
    while (!cts.Token.IsCancellationRequested)
    {
        await Task.Delay(6000, cts.Token);
        Console.WriteLine($"[info] Message processed #{++tick} — queue depth: {tick % 7}");
    }
}
catch (OperationCanceledException) { }

Console.WriteLine("[info] USB.Uppbord.Host.NSB stopped");
