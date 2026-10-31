using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace ModernYmpd;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private readonly AppSettings settings = AppSettings.Load();
    private readonly MpdClient client;
    private readonly ShuffleService shuffle;
    private readonly DispatcherTimer refreshTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private bool refreshing;
    private int queueVersion = -1;
    private int currentSongId = int.MinValue;
    private string browserPath = "";
    private MpdStatus? currentStatus;

    private TextBox HostInput => this.FindControl<TextBox>("HostBox")!;
    private TextBox PortInput => this.FindControl<TextBox>("PortBox")!;
    private TextBox PasswordInput => this.FindControl<TextBox>("PasswordBox")!;
    private TextBox TargetInput => this.FindControl<TextBox>("FillTargetBox")!;
    private TextBox SearchInput => this.FindControl<TextBox>("SearchBox")!;
    private CheckBox AutoFillInput => this.FindControl<CheckBox>("AutoFillCheck")!;
    private CheckBox FilePathInput => this.FindControl<CheckBox>("ShowFilePathCheck")!;
    private CheckBox RandomInput => this.FindControl<CheckBox>("RandomCheck")!;
    private CheckBox RepeatInput => this.FindControl<CheckBox>("RepeatCheck")!;
    private CheckBox ConsumeInput => this.FindControl<CheckBox>("ConsumeCheck")!;
    private Slider SeekInput => this.FindControl<Slider>("SeekSlider")!;
    private Slider VolumeInput => this.FindControl<Slider>("VolumeSlider")!;
    private ListBox QueueInput => this.FindControl<ListBox>("QueueList")!;
    private TabControl PageInput => this.FindControl<TabControl>("Pages")!;

    public new event PropertyChangedEventHandler? PropertyChanged;
    public ObservableCollection<MpdSong> Queue { get; } = [];
    public ObservableCollection<MpdSong> SearchResults { get; } = [];
    public ObservableCollection<MpdEntry> Folders { get; } = [];
    public ObservableCollection<MpdEntry> Playlists { get; } = [];
    public ObservableCollection<MpdSong> BrowseSongs { get; } = [];

    private string connectionStatus = "Connecting…";
    public string ConnectionStatus { get => connectionStatus; private set => Set(ref connectionStatus, value); }
    private string message = "Ready";
    public string Message { get => message; private set => Set(ref message, value); }
    private string trackTitle = "No track selected";
    public string TrackTitle { get => trackTitle; private set => Set(ref trackTitle, value); }
    private string trackSubtitle = "Connect to your MPD server to get started";
    public string TrackSubtitle { get => trackSubtitle; private set => Set(ref trackSubtitle, value); }
    private string composerText = "";
    public string ComposerText { get => composerText; private set { Set(ref composerText, value); Notify(nameof(HasComposer)); } }
    public bool HasComposer => !string.IsNullOrEmpty(ComposerText);
    private string filePathText = "";
    public string FilePathText { get => filePathText; private set { Set(ref filePathText, value); Notify(nameof(IsFilePathVisible)); } }
    public bool IsFilePathVisible => settings.ShowFilePath && !string.IsNullOrEmpty(FilePathText);
    private string bitrateText = "";
    public string BitrateText { get => bitrateText; private set { Set(ref bitrateText, value); Notify(nameof(HasBitrate)); } }
    public bool HasBitrate => !string.IsNullOrEmpty(BitrateText);
    private string playPauseIcon = "▶";
    public string PlayPauseIcon { get => playPauseIcon; private set => Set(ref playPauseIcon, value); }
    private string elapsedText = "0:00";
    public string ElapsedText { get => elapsedText; private set => Set(ref elapsedText, value); }
    private string durationText = "0:00";
    public string DurationText { get => durationText; private set => Set(ref durationText, value); }
    private double elapsedSeconds;
    public double ElapsedSeconds { get => elapsedSeconds; private set => Set(ref elapsedSeconds, value); }
    private double trackDuration = 1;
    public double TrackDuration { get => trackDuration; private set => Set(ref trackDuration, Math.Max(1, value)); }
    private int volume;
    public int Volume { get => volume; private set { Set(ref volume, value); Notify(nameof(VolumeText)); } }
    public string VolumeText => Volume < 0 ? "Unavailable" : $"{Volume}%";
    private string queueHeading = "Queue";
    public string QueueHeading { get => queueHeading; private set => Set(ref queueHeading, value); }
    private string browserPathDisplay = "Library";
    public string BrowserPathDisplay { get => browserPathDisplay; private set => Set(ref browserPathDisplay, value); }
    private string searchHeading = "Search your MPD library";
    public string SearchHeading { get => searchHeading; private set => Set(ref searchHeading, value); }
    private string shuffleStatus = "Off";
    public string ShuffleStatus { get => shuffleStatus; private set => Set(ref shuffleStatus, value); }

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
        client = new MpdClient(settings);
        shuffle = new ShuffleService(client, settings);
        shuffle.StateChanged += text => Dispatcher.UIThread.Post(() => Message = text);
        HostInput.Text = settings.Host;
        PortInput.Text = settings.Port.ToString(CultureInfo.InvariantCulture);
        TargetInput.Text = settings.FillTarget.ToString(CultureInfo.InvariantCulture);
        AutoFillInput.IsChecked = settings.AutoFillEnabled;
        FilePathInput.IsChecked = settings.ShowFilePath;
        UpdateShuffleStatus();
        refreshTimer.Tick += async (_, _) => await RefreshAsync();
        Opened += async (_, _) =>
        {
            await RefreshAsync();
            await BrowseAsync("");
            shuffle.Start();
            refreshTimer.Start();
        };
        Closed += (_, _) => { refreshTimer.Stop(); shuffle.Dispose(); };
    }

    private void Set<T>(ref T field, T value, [CallerMemberName] string? property = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        Notify(property);
    }
    private void Notify(string? name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    private static string Clock(double seconds) => $"{(int)seconds / 60}:{(int)seconds % 60:00}";
    private static void Replace<T>(ObservableCollection<T> collection, IEnumerable<T> items)
    {
        collection.Clear();
        foreach (var item in items) collection.Add(item);
    }

    private async Task RefreshAsync()
    {
        if (refreshing) return;
        refreshing = true;
        try
        {
            var status = await client.GetStatusAsync();
            currentStatus = status;
            ConnectionStatus = $"Connected · {settings.Host}:{settings.Port}";
            PlayPauseIcon = status.State == "play" ? "⏸" : "▶";
            BitrateText = status.Bitrate > 0 && status.State != "stop" ? $"{status.Bitrate} kbit/s" : "";
            Volume = status.Volume;
            RandomInput.IsChecked = status.Random;
            RepeatInput.IsChecked = status.Repeat;
            ConsumeInput.IsChecked = status.Consume;
            TrackDuration = status.Duration;
            ElapsedSeconds = Math.Clamp(status.Elapsed, 0, TrackDuration);
            ElapsedText = Clock(status.Elapsed);
            DurationText = Clock(status.Duration);
            if (status.SongId != currentSongId)
            {
                currentSongId = status.SongId;
                var song = await client.GetCurrentSongAsync();
                TrackTitle = song?.DisplayTitle ?? "No track selected";
                TrackSubtitle = song is null ? "Choose music from Queue, Library or Search"
                    : string.Join("  ·  ", new[] { song.Artist, song.Album }
                        .Where(value => !string.IsNullOrWhiteSpace(value)));
                ComposerText = string.IsNullOrWhiteSpace(song?.Composer) ? "" : $"Composed by {song.Composer}";
                FilePathText = song?.File ?? "";
            }
            if (status.QueueVersion != queueVersion)
            {
                queueVersion = status.QueueVersion;
                Replace(Queue, await client.GetQueueAsync());
                QueueHeading = $"Queue · {Queue.Count} tracks";
            }
            QueueInput.SelectedItem = Queue.FirstOrDefault(song => song.Id == status.SongId);
        }
        catch (Exception error)
        {
            ConnectionStatus = "Disconnected";
            Message = error.Message;
        }
        finally { refreshing = false; }
    }

    private async Task ActAsync(Func<Task> action, string success)
    {
        try { await action(); Message = success; await RefreshAsync(); }
        catch (Exception error) { Message = error.Message; }
    }

    private async Task BrowseAsync(string path)
    {
        try
        {
            var entries = await client.BrowseAsync(path);
            browserPath = path;
            BrowserPathDisplay = string.IsNullOrEmpty(path) ? "Library" : path;
            Replace(Folders, entries.Where(item => item.Kind == "directory"));
            Replace(Playlists, entries.Where(item => item.Kind == "playlist"));
            Replace(BrowseSongs, entries.Where(item => item.Song is not null).Select(item => item.Song!));
        }
        catch (Exception error) { Message = error.Message; }
    }
    private void UpdateShuffleStatus() => ShuffleStatus = settings.AutoFillEnabled
        ? $"On · keep {settings.FillTarget} tracks in the queue" : "Off · enable in Settings";

    private async void Previous_Click(object? sender, RoutedEventArgs e) => await ActAsync(() => client.PreviousAsync(), "Previous track");
    private async void Stop_Click(object? sender, RoutedEventArgs e) => await ActAsync(() => client.StopAsync(), "Stopped");
    private async void Next_Click(object? sender, RoutedEventArgs e) => await ActAsync(() => client.NextAsync(), "Next track");
    private async void PlayPause_Click(object? sender, RoutedEventArgs e) => await ActAsync(async () =>
    {
        if (currentStatus?.State == "play") await client.PauseAsync(true);
        else if (currentStatus?.State == "pause") await client.PauseAsync(false);
        else await client.PlayAsync();
    }, "Playback updated");
    private async void Seek_Released(object? sender, PointerReleasedEventArgs e)
    {
        if (currentStatus?.SongId is >= 0)
            await ActAsync(() => client.SeekAsync(currentStatus.SongId, SeekInput.Value), "Position updated");
    }
    private async void Volume_Released(object? sender, PointerReleasedEventArgs e) =>
        await ActAsync(() => client.SetVolumeAsync((int)VolumeInput.Value), "Volume updated");
    private async void Random_Click(object? sender, RoutedEventArgs e) =>
        await ActAsync(() => client.SetRandomAsync(RandomInput.IsChecked == true), "Random mode updated");
    private async void Repeat_Click(object? sender, RoutedEventArgs e) =>
        await ActAsync(() => client.SetRepeatAsync(RepeatInput.IsChecked == true), "Repeat mode updated");
    private async void Consume_Click(object? sender, RoutedEventArgs e) =>
        await ActAsync(() => client.SetConsumeAsync(ConsumeInput.IsChecked == true), "Consume mode updated");

    private async void QueuePlay_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.DataContext is MpdSong song)
            await ActAsync(() => client.PlayIdAsync(song.Id), $"Playing {song.DisplayTitle}");
    }
    private async void QueueRemove_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.DataContext is MpdSong song)
            await ActAsync(() => client.DeleteIdAsync(song.Id), $"Removed {song.DisplayTitle}");
    }
    private async Task MoveQueueSongAsync(object? sender, int direction)
    {
        if ((sender as Button)?.DataContext is not MpdSong song) return;
        try
        {
            var queue = await client.GetQueueAsync();
            var position = queue.ToList().FindIndex(item => item.Id == song.Id);
            var target = position + direction;
            if (position < 0) throw new IOException("The track is no longer in the queue.");
            if (target < 0 || target >= queue.Count)
            {
                Message = direction < 0 ? "Already at the top of the queue" : "Already at the bottom of the queue";
                return;
            }
            await ActAsync(() => client.MoveIdAsync(song.Id, target),
                $"Moved {song.DisplayTitle} {(direction < 0 ? "up" : "down")}");
        }
        catch (Exception error) { Message = error.Message; }
    }
    private async void QueueUp_Click(object? sender, RoutedEventArgs e) =>
        await MoveQueueSongAsync(sender, -1);
    private async void QueueDown_Click(object? sender, RoutedEventArgs e) =>
        await MoveQueueSongAsync(sender, 1);
    private async void ClearQueue_Click(object? sender, RoutedEventArgs e)
    {
        settings.AutoFillEnabled = false;
        AutoFillInput.IsChecked = false;
        settings.Save();
        UpdateShuffleStatus();
        await ActAsync(() => client.ClearAsync(), "Queue cleared; library shuffle switched off");
    }

    private async void BrowseFolder_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.DataContext is MpdEntry entry) await BrowseAsync(entry.Path);
    }
    private async void BrowsePlaylist_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.DataContext is MpdEntry entry)
            await ActAsync(() => client.LoadPlaylistAsync(entry.Path), $"Added playlist {entry.DisplayName}");
    }
    private async void BrowseBack_Click(object? sender, RoutedEventArgs e)
    {
        var slash = browserPath.LastIndexOf('/');
        await BrowseAsync(slash < 0 ? "" : browserPath[..slash]);
    }
    private async void BrowseRefresh_Click(object? sender, RoutedEventArgs e) => await BrowseAsync(browserPath);
    private async void BrowseNext_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.DataContext is MpdSong song)
            await ActAsync(() => client.AddNextAsync(song.File), $"Queued next: {song.DisplayTitle}");
    }
    private async void BrowseAdd_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.DataContext is MpdSong song)
            await ActAsync(() => client.AddAsync(song.File), $"Added: {song.DisplayTitle}");
    }

    private async Task SearchAsync()
    {
        try
        {
            var results = await client.SearchAsync(SearchInput.Text ?? "");
            Replace(SearchResults, results);
            SearchHeading = $"{results.Count} results" + (results.Count == 300 ? " · showing first 300" : "");
        }
        catch (Exception error) { Message = error.Message; }
    }
    private async void Search_Click(object? sender, RoutedEventArgs e) => await SearchAsync();
    private async void Search_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) await SearchAsync();
    }
    private async void SearchNext_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.DataContext is MpdSong song)
            await ActAsync(() => client.AddNextAsync(song.File), $"Queued next: {song.DisplayTitle}");
    }
    private async void SearchAdd_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.DataContext is MpdSong song)
            await ActAsync(() => client.AddAsync(song.File), $"Added: {song.DisplayTitle}");
    }
    private async void SearchPlay_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.DataContext is MpdSong song)
            await ActAsync(() => client.AddAndPlayAsync(song.File), $"Playing {song.DisplayTitle}");
    }

    private async void SaveSettings_Click(object? sender, RoutedEventArgs e)
    {
        if (!int.TryParse(PortInput.Text, out var port) || port is < 1 or > 65535 ||
            !int.TryParse(TargetInput.Text, out var target) || target is < 1 or > 500 ||
            string.IsNullOrWhiteSpace(HostInput.Text))
        {
            Message = "Enter a host, port (1–65535), and queue target (1–500).";
            return;
        }
        settings.Host = HostInput.Text.Trim();
        settings.Port = port;
        settings.Password = PasswordInput.Text ?? "";
        settings.FillTarget = target;
        settings.AutoFillEnabled = AutoFillInput.IsChecked == true;
        settings.ShowFilePath = FilePathInput.IsChecked == true;
        settings.Save();
        queueVersion = -1;
        currentSongId = int.MinValue;
        Notify(nameof(IsFilePathVisible));
        UpdateShuffleStatus();
        Message = "Settings saved";
        await RefreshAsync();
        await BrowseAsync("");
    }
    private async void UpdateDatabase_Click(object? sender, RoutedEventArgs e) =>
        await ActAsync(() => client.UpdateDatabaseAsync(), "MPD database update started");
    private async void ReloadLibrary_Click(object? sender, RoutedEventArgs e)
    {
        try { Message = $"MPD library: {await client.GetLibraryCountAsync()} tracks"; }
        catch (Exception error) { Message = error.Message; }
    }
    private void OpenSettings_Click(object? sender, RoutedEventArgs e) => PageInput.SelectedIndex = 3;
}
