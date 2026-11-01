using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace ModernYmpd;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow();
            desktop.MainWindow = window;
            desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
            if (Program.ScreenshotDirectory is not null)
            {
                base.OnFrameworkInitializationCompleted();
                return;
            }
            var quitting = false;
            var promptOpen = false;
            async Task AskBeforeExitAsync()
            {
                if (promptOpen) return;
                promptOpen = true;
                try
                {
                    window.Activate();
                    var choice = await new ExitDialog(window.PauseMpdBeforeExitAsync)
                        .ShowDialog<ExitChoice>(window);
                    if (choice == ExitChoice.Quit)
                    {
                        quitting = true;
                        desktop.Shutdown();
                    }
                }
                catch (Exception error)
                {
                    CrashLog.Write(error, "Exit dialog");
                    window.ShowError(error.Message);
                }
                finally { promptOpen = false; }
            }
            window.Closing += (_, e) =>
            {
                if (quitting || !window.AskPauseOnExit)
                {
                    window.BeginShutdown();
                    return;
                }
                e.Cancel = true;
                _ = AskBeforeExitAsync();
            };
            desktop.ShutdownRequested += (_, e) =>
            {
                if (quitting || !window.AskPauseOnExit) return;
                e.Cancel = true;
                _ = AskBeforeExitAsync();
            };
            if (Program.SmokeClose)
                window.Opened += async (_, _) =>
                {
                    await Task.Delay(TimeSpan.FromSeconds(2));
                    window.Close();
                };

            MacDockIcon.Apply();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
