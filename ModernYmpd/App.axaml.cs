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
            window.Closing += async (_, e) =>
            {
                if (quitting || !window.AskPauseOnExit) return;
                e.Cancel = true;
                await AskBeforeExitAsync();
            };
            desktop.ShutdownRequested += async (_, e) =>
            {
                if (quitting || !window.AskPauseOnExit) return;
                e.Cancel = true;
                await AskBeforeExitAsync();
            };

            MacDockIcon.Apply();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
