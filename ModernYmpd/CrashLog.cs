namespace ModernYmpd;

internal static class CrashLog
{
    public static string FilePath => Path.Combine(
        Path.GetDirectoryName(AppSettings.FilePath)!, "errors.log");

    public static void Write(Exception error, string context)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.AppendAllText(FilePath,
                $"[{DateTimeOffset.Now:O}] {context}{Environment.NewLine}{error}{Environment.NewLine}{Environment.NewLine}");
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(FilePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch { /* Logging must never cause another crash. */ }
    }
}
