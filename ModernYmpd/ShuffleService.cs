namespace ModernYmpd;

public sealed class ShuffleService(MpdClient client, AppSettings settings) : IDisposable
{
    private readonly CancellationTokenSource cancellation = new();
    private readonly SemaphoreSlim fillGate = new(1, 1);
    private readonly Queue<string> recent = new();
    private Task? worker;

    public event Action<string>? StateChanged;

    public void Start() => worker ??= Task.Run(RunAsync);

    private async Task RunAsync()
    {
        while (!cancellation.IsCancellationRequested)
        {
            try
            {
                if (settings.AutoFillEnabled)
                    await FillToAsync(settings.FillTarget, true, cancellation.Token);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { break; }
            catch (Exception error) { StateChanged?.Invoke($"Auto fill: {error.Message}"); }
            try { await Task.Delay(TimeSpan.FromSeconds(5), cancellation.Token); }
            catch (OperationCanceledException) { break; }
        }
    }

    public async Task<int> FillToAsync(int target, bool startPlaybackIfEmpty = false,
        CancellationToken token = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, cancellation.Token);
        await fillGate.WaitAsync(linked.Token);
        try { return await FillAsync(target, startPlaybackIfEmpty, linked.Token); }
        finally { fillGate.Release(); }
    }

    private async Task<int> FillAsync(int target, bool startPlaybackIfEmpty, CancellationToken token)
    {
        var status = await client.GetStatusAsync(token);
        var missing = target - status.QueueLength;
        if (missing <= 0) return 0;
        var libraryCount = await client.GetLibraryCountAsync(token);
        if (libraryCount == 0)
            throw new IOException("MPD library is empty. Update the database in Settings.");
        var queued = (await client.GetQueueAsync(token)).Select(song => song.File)
            .ToHashSet(StringComparer.Ordinal);
        var added = 0;
        for (var i = 0; i < missing; i++)
        {
            MpdSong? chosen = null;
            for (var attempt = 0; attempt < 30; attempt++)
            {
                var candidate = await client.GetLibrarySongAtAsync(Random.Shared.Next(libraryCount), token);
                if (candidate is null) continue;
                chosen ??= candidate;
                if (!queued.Contains(candidate.File) && !recent.Contains(candidate.File))
                {
                    chosen = candidate;
                    break;
                }
            }
            if (chosen is null) throw new IOException("MPD returned no library track.");
            await client.AddAsync(chosen.File, token);
            queued.Add(chosen.File);
            recent.Enqueue(chosen.File);
            if (recent.Count > 50) recent.Dequeue();
            added++;
        }
        if (startPlaybackIfEmpty && status.State == "stop" && status.QueueLength == 0 && added > 0)
            await client.PlayAsync(token);
        if (startPlaybackIfEmpty)
            StateChanged?.Invoke($"Added {added} random track{(added == 1 ? "" : "s")}.");
        return added;
    }

    public void Dispose()
    {
        cancellation.Cancel();
    }
}
