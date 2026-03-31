using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using WinRT;

namespace BengiDevTools.WinUI;

public class App : MauiWinUIApplication
{
    [global::System.STAThreadAttribute]
    static void Main(string[] args)
    {
        ComWrappersSupport.InitializeComWrappers();
        Microsoft.UI.Xaml.Application.Start(p =>
        {
            var context = new DispatcherQueueSynchronizationContext(
                DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            _ = new App();
        });
    }

    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
