using Avalonia;
using System;

namespace ModernYmpd;

class Program
{
    public static string? ScreenshotDirectory { get; private set; }
    public static bool SmokeClose { get; private set; }
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static int Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception error)
                CrashLog.Write(error, "Unhandled exception");
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            CrashLog.Write(e.Exception, "Unobserved task exception");
            e.SetObserved();
        };
        if (args.Length == 2 && args[0] == "--render-screenshots")
        {
            ScreenshotDirectory = Path.GetFullPath(args[1]);
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime([]);
        }
        if (args.Length == 1 && args[0] == "--smoke-close")
        {
            SmokeClose = true;
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime([]);
        }
        if (args.Length > 0 && args[0] == "--check-mpd")
        {
            try
            {
                var client = new MpdClient(AppSettings.Load());
                var status = client.GetStatusAsync().GetAwaiter().GetResult();
                var queue = client.GetQueueAsync().GetAwaiter().GetResult();
                var root = client.BrowseAsync("").GetAwaiter().GetResult();
                var libraryCount = client.GetLibraryCountAsync().GetAwaiter().GetResult();
                var sample = libraryCount > 0 ? client.GetLibrarySongAtAsync(libraryCount / 2).GetAwaiter().GetResult() : null;
                var query = args.Length > 1 ? args[1] : "music";
                var matches = client.SearchAsync(query).GetAwaiter().GetResult();
                Console.WriteLine($"MPD {status.State}; queue {queue.Count}; library root {root.Count}; library songs {libraryCount}; sample '{sample?.DisplayTitle}'; search '{query}' {matches.Count}");
                return 0;
            }
            catch (Exception error)
            {
                Console.Error.WriteLine(error.Message);
                return 1;
            }
        }
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .With(new MacOSPlatformOptions { ShowInDock = true })
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
