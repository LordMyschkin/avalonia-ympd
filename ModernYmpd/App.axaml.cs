using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;

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
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            if (Program.ScreenshotDirectory is not null)
            {
                base.OnFrameworkInitializationCompleted();
                return;
            }
            var quitting = false;
            var promptOpen = false;
            async Task AskBeforeExitAsync()
            {
                if (promptOpen) { window.Activate(); return; }
                promptOpen = true;
                var wasVisible = window.IsVisible;
                try
                {
                    if (!wasVisible) window.Show();
                    window.Activate();
                    var choice = await new ExitDialog(window.AutoFillEnabled).ShowDialog<ExitChoice>(window);
                    if (choice == ExitChoice.KeepRunning) window.Hide();
                    else if (choice == ExitChoice.Quit)
                    {
                        quitting = true;
                        desktop.Shutdown();
                    }
                    else if (!wasVisible) window.Hide();
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
                if (quitting) return;
                e.Cancel = true;
                await AskBeforeExitAsync();
            };
            desktop.ShutdownRequested += async (_, e) =>
            {
                if (quitting) return;
                e.Cancel = true;
                await AskBeforeExitAsync();
            };

            var show = new NativeMenuItem("Show Modern YMPD");
            show.Click += (_, _) => { window.Show(); window.Activate(); };
            var quit = new NativeMenuItem("Quit Modern YMPD");
            quit.Click += async (_, _) => await AskBeforeExitAsync();
            var menu = new NativeMenu();
            menu.Items.Add(show);
            menu.Items.Add(new NativeMenuItemSeparator());
            menu.Items.Add(quit);
            var tray = new TrayIcon
            {
                Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://ModernYmpd/Assets/app.ico"))),
                ToolTipText = "Modern YMPD",
                Menu = menu
            };
            TrayIcon.SetIcons(this, new TrayIcons { tray });
            MacDockIcon.Apply();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
