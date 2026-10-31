using System.Globalization;
using System.Net.Sockets;
using System.Text;

namespace ModernYmpd;

public sealed record MpdSong(
    string File,
    string Title,
    string Artist,
    string Album,
    string Composer,
    int Id,
    int Position,
    double Duration)
{
    public string DisplayTitle => string.IsNullOrWhiteSpace(Title)
        ? Path.GetFileName(File)
        : Title;

    public string DisplayArtist => string.IsNullOrWhiteSpace(Artist) ? "Unknown artist" : Artist;
    public string DurationText => Duration > 0
        ? $"{(int)Duration / 60}:{(int)Duration % 60:00}"
        : "–";
    public string PositionText => Position >= 0 ? (Position + 1).ToString(CultureInfo.InvariantCulture) : "";
}

public sealed record MpdEntry(string Kind, string Path, MpdSong? Song)
{
    public string DisplayName => Kind == "directory"
        ? System.IO.Path.GetFileName(Path.TrimEnd('/'))
        : Kind == "playlist"
            ? System.IO.Path.GetFileName(Path)
            : Song?.DisplayTitle ?? Path;
    public string Subtitle => Kind == "file"
        ? string.Join(" · ", new[] { Song?.Artist, Song?.Album }.Where(x => !string.IsNullOrWhiteSpace(x)))
        : Kind == "directory" ? "Folder" : "Playlist";
}

public sealed record MpdStatus(
    string State,
    int Volume,
    int SongId,
    int SongPosition,
    int QueueLength,
    int QueueVersion,
    double Elapsed,
    double Duration,
    int Bitrate,
    bool Random,
    bool Repeat,
    bool Consume);

public sealed class MpdClient(AppSettings settings)
{
    public async Task<MpdStatus> GetStatusAsync(CancellationToken token = default)
    {
        var values = Fields(await SendAsync("status", token));
        return new MpdStatus(
            Get(values, "state"),
            Int(values, "volume", -1),
            Int(values, "songid", -1),
            Int(values, "song", -1),
            Int(values, "playlistlength", 0),
            Int(values, "playlist", 0),
            Number(values, "elapsed"),
            Number(values, "duration"),
            Int(values, "bitrate", 0),
            Int(values, "random", 0) == 1,
            Int(values, "repeat", 0) == 1,
            Int(values, "consume", 0) == 1);
    }

    public async Task<MpdSong?> GetCurrentSongAsync(CancellationToken token = default) =>
        ParseSongs(await SendAsync("currentsong", token)).FirstOrDefault();

    public async Task<IReadOnlyList<MpdSong>> GetQueueAsync(CancellationToken token = default) =>
        ParseSongs(await SendAsync("playlistinfo", token));

    private const string AllFilesFilter = "\"(file != '')\"";

    public async Task<int> GetLibraryCountAsync(CancellationToken token = default) =>
        Int(Fields(await SendAsync($"searchcount {AllFilesFilter}", token)), "songs");

    public async Task<MpdSong?> GetLibrarySongAtAsync(int index, CancellationToken token = default) =>
        ParseSongs(await SendAsync($"search {AllFilesFilter} window {index}:{index + 1}", token))
            .FirstOrDefault();

    public async Task<IReadOnlyList<MpdSong>> SearchAsync(string query, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];
        return ParseSongs(await SendAsync($"search any {Quote(query.Trim())} window 0:300", token));
    }

    public async Task<IReadOnlyList<MpdEntry>> BrowseAsync(string path, CancellationToken token = default)
    {
        var command = string.IsNullOrEmpty(path) ? "lsinfo" : $"lsinfo {Quote(path)}";
        return ParseEntries(await SendAsync(command, token));
    }

    public async Task AddAsync(string file, CancellationToken token = default) =>
        await SendAsync($"addid {Quote(file)}", token);

    public async Task AddAndPlayAsync(string file, CancellationToken token = default)
    {
        var values = Fields(await SendAsync($"addid {Quote(file)}", token));
        var id = Int(values, "Id", -1);
        if (id < 0) throw new IOException("MPD did not return a queue id.");
        await PlayIdAsync(id, token);
    }

    public async Task AddNextAsync(string file, CancellationToken token = default)
    {
        var status = await GetStatusAsync(token);
        var position = status.SongPosition >= 0 ? $" {status.SongPosition + 1}" : "";
        await SendAsync($"addid {Quote(file)}{position}", token);
    }

    public async Task PlayAsync(CancellationToken token = default) => await SendAsync("play", token);
    public async Task PlayIdAsync(int id, CancellationToken token = default) => await SendAsync($"playid {id}", token);
    public async Task PauseAsync(bool pause, CancellationToken token = default) => await SendAsync($"pause {(pause ? 1 : 0)}", token);
    public async Task StopAsync(CancellationToken token = default) => await SendAsync("stop", token);
    public async Task NextAsync(CancellationToken token = default) => await SendAsync("next", token);
    public async Task PreviousAsync(CancellationToken token = default) => await SendAsync("previous", token);
    public async Task DeleteIdAsync(int id, CancellationToken token = default) => await SendAsync($"deleteid {id}", token);
    public async Task MoveIdAsync(int id, int position, CancellationToken token = default) =>
        await SendAsync($"moveid {id} {position}", token);
    public async Task ClearAsync(CancellationToken token = default) => await SendAsync("clear", token);
    public async Task SetVolumeAsync(int volume, CancellationToken token = default) =>
        await SendAsync($"setvol {Math.Clamp(volume, 0, 100)}", token);
    public async Task SetRandomAsync(bool enabled, CancellationToken token = default) =>
        await SendAsync($"random {(enabled ? 1 : 0)}", token);
    public async Task SetRepeatAsync(bool enabled, CancellationToken token = default) =>
        await SendAsync($"repeat {(enabled ? 1 : 0)}", token);
    public async Task SetConsumeAsync(bool enabled, CancellationToken token = default) =>
        await SendAsync($"consume {(enabled ? 1 : 0)}", token);
    public async Task SeekAsync(int id, double seconds, CancellationToken token = default) =>
        await SendAsync($"seekid {id} {Math.Max(0, (int)seconds)}", token);
    public async Task UpdateDatabaseAsync(CancellationToken token = default) => await SendAsync("update", token);
    public async Task LoadPlaylistAsync(string path, CancellationToken token = default) =>
        await SendAsync($"load {Quote(path)}", token);

    private async Task<List<string>> SendAsync(string command, CancellationToken token, TimeSpan? timeout = null)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeoutSource.CancelAfter(timeout ?? TimeSpan.FromSeconds(12));
        var ct = timeoutSource.Token;
        using var tcp = new TcpClient { NoDelay = true };
        await tcp.ConnectAsync(settings.Host, settings.Port, ct);
        await using var stream = tcp.GetStream();
        using var reader = new StreamReader(stream, new UTF8Encoding(false), leaveOpen: true);
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true)
        {
            AutoFlush = true,
            NewLine = "\n"
        };
        var greeting = await reader.ReadLineAsync(ct);
        if (greeting is null || !greeting.StartsWith("OK MPD ", StringComparison.Ordinal))
            throw new IOException("The server did not send an MPD greeting.");
        if (!string.IsNullOrEmpty(settings.Password))
        {
            await writer.WriteLineAsync($"password {Quote(settings.Password)}".AsMemory(), ct);
            await ReadResponseAsync(reader, ct);
        }
        await writer.WriteLineAsync(command.AsMemory(), ct);
        return await ReadResponseAsync(reader, ct);
    }

    private static async Task<List<string>> ReadResponseAsync(StreamReader reader, CancellationToken token)
    {
        var lines = new List<string>();
        while (true)
        {
            var line = await reader.ReadLineAsync(token) ?? throw new IOException("MPD closed the connection.");
            if (line == "OK") return lines;
            if (line.StartsWith("ACK ", StringComparison.Ordinal)) throw new IOException(line);
            lines.Add(line);
        }
    }

    private static string Quote(string value)
    {
        if (value.Contains('\n') || value.Contains('\r')) throw new ArgumentException("MPD values cannot contain newlines.");
        return '"' + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + '"';
    }

    private static List<MpdSong> ParseSongs(List<string> lines) => ParseEntries(lines)
        .Where(entry => entry.Kind == "file" && entry.Song is not null)
        .Select(entry => entry.Song!)
        .ToList();

    private static List<MpdEntry> ParseEntries(List<string> lines)
    {
        var entries = new List<MpdEntry>();
        Dictionary<string, string>? fields = null;
        string kind = "";
        string path = "";
        void Finish()
        {
            if (fields is null) return;
            MpdSong? song = null;
            if (kind == "file")
                song = new MpdSong(path, Get(fields, "Title"), Get(fields, "Artist"),
                    Get(fields, "Album"), Get(fields, "Composer"), Int(fields, "Id", -1),
                    Int(fields, "Pos", -1), Number(fields, "duration", Number(fields, "Time")));
            entries.Add(new MpdEntry(kind, path, song));
        }
        foreach (var line in lines)
        {
            var split = line.IndexOf(": ", StringComparison.Ordinal);
            if (split < 0) continue;
            var key = line[..split];
            var value = line[(split + 2)..];
            if (key is "file" or "directory" or "playlist")
            {
                Finish();
                kind = key;
                path = value;
                fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
            else if (fields is not null && !fields.ContainsKey(key))
            {
                fields[key] = value;
            }
        }
        Finish();
        return entries;
    }

    private static Dictionary<string, string> Fields(List<string> lines)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines)
        {
            var split = line.IndexOf(": ", StringComparison.Ordinal);
            if (split >= 0) values[line[..split]] = line[(split + 2)..];
        }
        return values;
    }

    private static string Get(Dictionary<string, string> fields, string key) =>
        fields.TryGetValue(key, out var value) ? value : "";
    private static int Int(Dictionary<string, string> fields, string key, int fallback = 0) =>
        int.TryParse(Get(fields, key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : fallback;
    private static double Number(Dictionary<string, string> fields, string key, double fallback = 0) =>
        double.TryParse(Get(fields, key), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : fallback;
}
