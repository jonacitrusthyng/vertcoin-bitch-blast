using System.IO;
using System.Windows;
namespace VertcoinOcm;
public partial class App : Application
{
    private Mutex? single;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // Preserve the 0.2 instance lock so old and new UIs cannot run together.
        single = new Mutex(true, @"Local\VertcoinGardenMiner", out bool first);
        if (!first) { MessageBox.Show("Vertcoin Bitch Blast! Edition is already open."); Shutdown(); return; }
        DispatcherUnhandledException += (_, args) => { MessageBox.Show(args.Exception.Message, "Vertcoin Bitch Blast! Edition needs attention"); args.Handled = true; };
        MainWindow = new MainWindow(); MainWindow.Show();
    }
    protected override void OnExit(ExitEventArgs e) { single?.Dispose(); base.OnExit(e); }
}
