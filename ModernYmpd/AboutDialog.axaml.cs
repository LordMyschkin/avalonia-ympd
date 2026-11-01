using System.Reflection;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace ModernYmpd;

public partial class AboutDialog : Window
{
    private readonly CancellationTokenSource closing = new();

    public AboutDialog() : this(null, null) { }

    public AboutDialog(MpdClient? client, MpdStatistics? preview = null)
    {
        InitializeComponent();
        var version = typeof(App).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion.Split('+')[0]
            ?? typeof(App).Assembly.GetName().Version?.ToString(3)
            ?? "unknown";
        this.FindControl<TextBlock>("VersionText")!.Text = $"Version {version}";
        if (preview is not null) ShowStatistics(preview);
        else if (client is not null) Opened += async (_, _) => await LoadStatisticsAsync(client);
        else this.FindControl<TextBlock>("StatsText")!.Text = "Connect to MPD to see statistics.";
        Closed += (_, _) => closing.Cancel();
    }

    private async Task LoadStatisticsAsync(MpdClient client)
    {
        try
        {
            var statistics = await client.GetStatisticsAsync(closing.Token);
            if (!closing.IsCancellationRequested) ShowStatistics(statistics);
        }
        catch (OperationCanceledException) when (closing.IsCancellationRequested) { }
        catch (Exception error)
        {
            if (!closing.IsCancellationRequested)
                this.FindControl<TextBlock>("StatsText")!.Text = $"MPD statistics unavailable: {error.Message}";
        }
    }

    private void ShowStatistics(MpdStatistics stats)
    {
        static string Duration(long seconds)
        {
            var days = seconds / 86400;
            var hours = seconds % 86400 / 3600;
            var minutes = seconds % 3600 / 60;
            return days > 0 ? $"{days}d {hours}h {minutes}m" : $"{hours}h {minutes}m";
        }

        var updated = stats.DatabaseUpdated?.ToLocalTime()
            .ToString("g", CultureInfo.CurrentCulture) ?? "Unknown";
        this.FindControl<TextBlock>("StatsText")!.Text = string.Join(Environment.NewLine,
            $"Artists: {stats.Artists:N0}",
            $"Albums: {stats.Albums:N0}",
            $"Songs: {stats.Songs:N0}",
            $"Play time: {Duration(stats.PlaytimeSeconds)}",
            $"Uptime: {Duration(stats.UptimeSeconds)}",
            $"DB updated: {updated}",
            $"DB play time: {Duration(stats.DatabasePlaytimeSeconds)}");
    }

    private void Close_Click(object? sender, RoutedEventArgs e) => Close();
}
