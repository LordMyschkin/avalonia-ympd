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
            var quitting = false;
            window.Closing += (_, e) =>
            {
                if (quitting) return;
                e.Cancel = true;
                window.Hide();
            };

            var show = new NativeMenuItem("Show Modern YMPD");
            show.Click += (_, _) => { window.Show(); window.Activate(); };
            var quit = new NativeMenuItem("Quit Modern YMPD");
            quit.Click += (_, _) => { quitting = true; desktop.Shutdown(); };
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
        }

        base.OnFrameworkInitializationCompleted();
    }
}
