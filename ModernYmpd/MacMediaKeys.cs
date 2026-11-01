using System.Runtime.InteropServices;
using Avalonia.Threading;

namespace ModernYmpd;

internal enum MediaCommand { Previous, Toggle, Next, Play, Pause }

internal sealed class MacMediaKeys : IDisposable
{
    private const string Library = "libModernYmpdMediaKeys.dylib";

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void CommandCallback(int command);

    [DllImport(Library)] private static extern void ympd_media_start(CommandCallback callback);
    [DllImport(Library)] private static extern void ympd_media_update(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string? title,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string? artist,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string? album,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string? composer,
        double duration, double elapsed, int state);
    [DllImport(Library)] private static extern void ympd_media_stop();

    private static readonly CommandCallback NativeCallback = OnCommand;
    private static MacMediaKeys? active;
    private readonly Func<MediaCommand, Task> handler;
    private int publishedSongId = int.MinValue;
    private string publishedState = "";
    private double publishedElapsed;
    private DateTimeOffset publishedAt;

    private MacMediaKeys(Func<MediaCommand, Task> handler) => this.handler = handler;

    public static MacMediaKeys? Start(Func<MediaCommand, Task> handler)
    {
        if (!OperatingSystem.IsMacOS() ||
            !File.Exists(Path.Combine(AppContext.BaseDirectory, Library))) return null;
        try
        {
            var instance = new MacMediaKeys(handler);
            active = instance;
            ympd_media_start(NativeCallback);
            return instance;
        }
        catch (Exception error)
        {
            active = null;
            CrashLog.Write(error, "macOS media keys initialization");
            return null;
        }
    }

    private static void OnCommand(int command)
    {
        try
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (active is { } instance && Enum.IsDefined((MediaCommand)command))
                    _ = instance.handler((MediaCommand)command);
            });
        }
        catch (Exception error) { CrashLog.Write(error, "macOS media key callback"); }
    }

    public void Update(MpdSong? song, MpdStatus status)
    {
        var now = DateTimeOffset.UtcNow;
        var expectedElapsed = publishedElapsed +
            (publishedState == "play" ? (now - publishedAt).TotalSeconds : 0);
        if (publishedSongId == status.SongId && publishedState == status.State &&
            Math.Abs(status.Elapsed - expectedElapsed) < 3) return;
        try
        {
            ympd_media_update(song?.DisplayTitle, song?.Artist, song?.Album, song?.Composer,
                song?.Duration ?? status.Duration, status.Elapsed,
                status.State == "play" ? 1 : status.State == "pause" ? 2 : 0);
            publishedSongId = status.SongId;
            publishedState = status.State;
            publishedElapsed = status.Elapsed;
            publishedAt = now;
        }
        catch (Exception error) { CrashLog.Write(error, "macOS Now Playing update"); }
    }

    public void Clear()
    {
        try { ympd_media_update(null, null, null, null, 0, 0, 0); }
        catch (Exception error) { CrashLog.Write(error, "macOS Now Playing clear"); }
        publishedSongId = int.MinValue;
        publishedState = "";
    }

    public void Dispose()
    {
        active = null;
        try { ympd_media_stop(); }
        catch (Exception error) { CrashLog.Write(error, "macOS media keys cleanup"); }
    }
}
