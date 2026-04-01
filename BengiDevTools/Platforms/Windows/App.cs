using Microsoft.UI.Dispatching;
using WinRT;

namespace BengiDevTools.WinUI;

public class App : MauiWinUIApplication
{
    [global::System.STAThreadAttribute]
    static void Main(string[] args)
    {
        try
        {
            ComWrappersSupport.InitializeComWrappers();
            global::Microsoft.UI.Xaml.Application.Start(p =>
            {
                try
                {
                    var context = new DispatcherQueueSynchronizationContext(
                        DispatcherQueue.GetForCurrentThread());
                    SynchronizationContext.SetSynchronizationContext(context);
                    _ = new App();
                }
                catch (Exception ex)
                {
                    WriteCrashLog(ex);
                }
            });
        }
        catch (Exception ex)
        {
            WriteCrashLog(ex);
        }
    }

    static void WriteCrashLog(Exception ex)
    {
        var path = Path.Combine(Path.GetTempPath(), "bengidevtools-crash.txt");
        File.WriteAllText(path, ex.ToString());
        System.Diagnostics.Process.Start("notepad.exe", path);
    }

    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
