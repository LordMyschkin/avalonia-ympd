using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Avalonia.VisualTree;

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
    private MpdSong? currentSong;
    private MacMediaKeys? mediaKeys;
    private MpdSong? dragSong;
    private IPointer? dragPointer;
    private Border? dragSource;
    private Border? dropTarget;
    private bool draggingQueueSong;
    private bool dropAfter;
    private Point dragStart;

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
    private Border DragPreview => this.FindControl<Border>("DragGhost")!;
    private TabControl PageInput => this.FindControl<TabControl>("Pages")!;

    public new event PropertyChangedEventHandler? PropertyChanged;
    public ObservableCollection<MpdSong> Queue { get; } = [];
    public ObservableCollection<MpdSong> SearchResults { get; } = [];
    public ObservableCollection<MpdEntry> Folders { get; } = [];
    public ObservableCollection<MpdEntry> Playlists { get; } = [];
    public ObservableCollection<MpdSong> BrowseSongs { get; } = [];
    public bool AutoFillEnabled => settings.AutoFillEnabled;

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
        if (OperatingSystem.IsMacOS())
        {
            PageInput.Classes.Add("native-navigation");
            InstallMacMenu();
            KeyDown += MediaKeyDown;
        }
        PageInput.SelectedIndex = 0;
        Opened += (_, _) => PageInput.SelectedIndex = 0;
        shuffle.StateChanged += text => Dispatcher.UIThread.Post(() => Message = text);
        HostInput.Text = settings.Host;
        PortInput.Text = settings.Port.ToString(CultureInfo.InvariantCulture);
        TargetInput.Text = settings.FillTarget.ToString(CultureInfo.InvariantCulture);
        AutoFillInput.IsChecked = settings.AutoFillEnabled;
        FilePathInput.IsChecked = settings.ShowFilePath;
        UpdateShuffleStatus();
        if (Program.ScreenshotDirectory is { } screenshotDirectory)
        {
            PopulateDemo();
            Opened += async (_, _) => await RenderDemoScreenshotsAsync(screenshotDirectory);
            return;
        }
        refreshTimer.Tick += async (_, _) => await RefreshAsync();
        Opened += async (_, _) =>
        {
            mediaKeys ??= MacMediaKeys.Start(HandleMediaCommandAsync);
            await RefreshAsync();
            await BrowseAsync("");
            shuffle.Start();
            refreshTimer.Start();
        };
        Closed += (_, _) => { refreshTimer.Stop(); shuffle.Dispose(); mediaKeys?.Dispose(); };
    }

    private void Set<T>(ref T field, T value, [CallerMemberName] string? property = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        Notify(property);
    }
    private void Notify(string? name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    public void ShowError(string error) => Message = error;
    private void InstallMacMenu()
    {
        var view = new NativeMenuItem("Ansicht") { Menu = new NativeMenu() };
        foreach (var (name, page) in new[]
        {
            ("Queue", 0), ("Bibliothek", 1), ("Suche", 2), ("Einstellungen", 3)
        })
        {
            var item = new NativeMenuItem(name);
            item.Click += (_, _) => PageInput.SelectedIndex = page;
            view.Menu.Items.Add(item);
        }

        var playback = new NativeMenuItem("Wiedergabe") { Menu = new NativeMenu() };
        void AddPlayback(string name, Func<Task> action)
        {
            var item = new NativeMenuItem(name);
            item.Click += async (_, _) => await ActAsync(action, name);
            playback.Menu.Items.Add(item);
        }
        AddPlayback("Vorheriger Titel", () => client.PreviousAsync());
        AddPlayback("Wiedergabe/Pause", async () =>
        {
            if (currentStatus?.State == "play") await client.PauseAsync(true);
            else if (currentStatus?.State == "pause") await client.PauseAsync(false);
            else await client.PlayAsync();
        });
        AddPlayback("Stopp", () => client.StopAsync());
        AddPlayback("Nächster Titel", () => client.NextAsync());

        var menu = new NativeMenu();
        menu.Items.Add(view);
        menu.Items.Add(playback);
        var aboutMenu = new NativeMenuItem("About modern ympd") { Menu = new NativeMenu() };
        var about = new NativeMenuItem("Über modern ympd…");
        about.Click += async (_, _) =>
        {
            try { await new AboutDialog().ShowDialog(this); }
            catch (Exception error)
            {
                CrashLog.Write(error, "About dialog");
                Message = error.Message;
            }
        };
        aboutMenu.Menu.Items.Add(about);
        menu.Items.Add(aboutMenu);
        NativeMenu.SetMenu(this, menu);
    }
    private void PopulateDemo()
    {
        settings.ShowFilePath = true;
        settings.AutoFillEnabled = true;
        FilePathInput.IsChecked = true;
        AutoFillInput.IsChecked = true;
        UpdateShuffleStatus();
        ConnectionStatus = "Preview · sample MPD library";
        Message = "Library shuffle keeps 20 tracks in the queue";
        TrackTitle = "Midnight Train";
        TrackSubtitle = "Lena Vale  ·  Afterglow";
        ComposerText = "Composed by Lena Vale";
        FilePathText = "Lena Vale/Afterglow/01 Midnight Train.flac";
        BitrateText = "320 kbit/s";
        PlayPauseIcon = "⏸";
        TrackDuration = 236;
        ElapsedSeconds = 82;
        ElapsedText = "1:22";
        DurationText = "3:56";
        Volume = 42;
        RandomInput.IsChecked = false;
        RepeatInput.IsChecked = false;
        ConsumeInput.IsChecked = true;

        var examples = new (string Title, string Artist, string Album, string Composer, int Duration)[]
        {
            ("Midnight Train", "Lena Vale", "Afterglow", "Lena Vale", 236),
            ("Blue Hour", "The North Lines", "Open Skies", "", 208),
            ("Paper Lanterns", "Mira Sol", "Small Hours", "Mira Sol", 252),
            ("Nocturne in Amber", "Arden Quartet", "Night Studies", "Elias Arden", 293),
            ("Homeward", "The North Lines", "Open Skies", "", 228),
            ("Glass River", "Mira Sol", "Small Hours", "Mira Sol", 201),
            ("Morning in June", "Lena Vale", "Afterglow", "Lena Vale", 247),
            ("Quiet Signals", "Arden Quartet", "Night Studies", "Elias Arden", 264),
            ("Silver Coast", "The North Lines", "Open Skies", "", 229),
            ("Last Light", "Mira Sol", "Small Hours", "Mira Sol", 218),
            ("Winding Road", "Lena Vale", "Afterglow", "Lena Vale", 233),
            ("Cloud Atlas", "Arden Quartet", "Night Studies", "Elias Arden", 278)
        };
        for (var i = 0; i < examples.Length; i++)
        {
            var item = examples[i];
            var song = new MpdSong($"{item.Artist}/{item.Album}/{i + 1:00} {item.Title}.flac",
                item.Title, item.Artist, item.Album, item.Composer, i + 100, i, item.Duration);
            Queue.Add(song);
            BrowseSongs.Add(song);
            if (item.Title.Contains('i', StringComparison.OrdinalIgnoreCase)) SearchResults.Add(song);
        }
        QueueHeading = $"Queue · {Queue.Count} tracks";
        QueueInput.SelectedItem = Queue[0];
        Folders.Add(new MpdEntry("directory", "Classical", null));
        Folders.Add(new MpdEntry("directory", "Jazz", null));
        Folders.Add(new MpdEntry("directory", "Indie", null));
        Folders.Add(new MpdEntry("directory", "Electronic", null));
        Playlists.Add(new MpdEntry("playlist", "Evening Mix", null));
        Playlists.Add(new MpdEntry("playlist", "Favorites", null));
        SearchInput.Text = "i";
        SearchHeading = $"{SearchResults.Count} results";
    }
    private async Task RenderDemoScreenshotsAsync(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            foreach (var (page, name) in new[] { (0, "queue.png"), (1, "library.png"), (2, "search.png"), (3, "settings.png") })
            {
                PageInput.SelectedIndex = page;
                await Task.Delay(300);
                using var bitmap = new RenderTargetBitmap(
                    new PixelSize((int)Bounds.Width, (int)Bounds.Height), new Vector(96, 96));
                bitmap.Render(this);
                bitmap.Save(Path.Combine(directory, name), PngBitmapEncoderOptions.Default);
                Console.WriteLine($"Rendered {name}");
            }
            var about = new AboutDialog();
            about.Show(this);
            await Task.Delay(300);
            using (var bitmap = new RenderTargetBitmap(
                new PixelSize((int)about.Bounds.Width, (int)about.Bounds.Height), new Vector(96, 96)))
            {
                bitmap.Render(about);
                bitmap.Save(Path.Combine(directory, "about.png"), PngBitmapEncoderOptions.Default);
            }
            about.Close();
            Console.WriteLine("Rendered about.png");
        }
        catch (Exception error) { Console.Error.WriteLine(error); }
        finally
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                desktop.Shutdown();
        }
    }
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
                var song = await client.GetCurrentSongAsync();
                currentSong = song;
                currentSongId = status.SongId;
                TrackTitle = song?.DisplayTitle ?? "No track selected";
                TrackSubtitle = song is null ? "Choose music from Queue, Library or Search"
                    : string.Join("  ·  ", new[] { song.Artist, song.Album }
                        .Where(value => !string.IsNullOrWhiteSpace(value)));
                ComposerText = string.IsNullOrWhiteSpace(song?.Composer) ? "" : $"Composed by {song.Composer}";
                FilePathText = song?.File ?? "";
            }
            mediaKeys?.Update(currentSong, status);
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
            mediaKeys?.Clear();
        }
        finally { refreshing = false; }
    }

    private async Task ActAsync(Func<Task> action, string success)
    {
        try { await action(); Message = success; await RefreshAsync(); }
        catch (Exception error) { Message = error.Message; }
    }

    private async Task HandleMediaCommandAsync(MediaCommand command)
    {
        switch (command)
        {
            case MediaCommand.Previous:
                await ActAsync(() => client.PreviousAsync(), "Previous track");
                break;
            case MediaCommand.Next:
                await ActAsync(() => client.NextAsync(), "Next track");
                break;
            case MediaCommand.Play:
                await ActAsync(() => client.PlayAsync(), "Playing");
                break;
            case MediaCommand.Pause:
                await ActAsync(() => client.PauseAsync(true), "Paused");
                break;
            case MediaCommand.Toggle:
                await ActAsync(async () =>
                {
                    var state = (await client.GetStatusAsync()).State;
                    if (state == "play") await client.PauseAsync(true);
                    else if (state == "pause") await client.PauseAsync(false);
                    else await client.PlayAsync();
                }, "Playback updated");
                break;
        }
    }

    private void MediaKeyDown(object? sender, KeyEventArgs e)
    {
        var command = e.Key switch
        {
            Key.F7 => MediaCommand.Previous,
            Key.F8 => MediaCommand.Toggle,
            Key.F9 => MediaCommand.Next,
            _ => (MediaCommand?)null
        };
        if (command is null) return;
        e.Handled = true;
        _ = HandleMediaCommandAsync(command.Value);
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
    private void Queue_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Border { DataContext: MpdSong song } ||
            !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed ||
            e.Source is Visual visual && visual.FindAncestorOfType<Button>(true) is not null) return;
        ClearQueueDrag();
        dragSong = song;
        dragPointer = e.Pointer;
        dragSource = (Border)sender;
        dragStart = e.GetPosition(QueueInput);
    }
    // Reorder within the list; native macOS drag sessions can abort before a drop reaches Avalonia.
    private void Queue_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (dragSong is null || e.Pointer != dragPointer) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            ClearQueueDrag();
            return;
        }
        var point = e.GetPosition(QueueInput);
        if (!draggingQueueSong)
        {
            var delta = point - dragStart;
            if (delta.X * delta.X + delta.Y * delta.Y < 36) return;
            draggingQueueSong = true;
            dragSource?.Classes.Add("queue-dragging");
            e.Pointer.Capture(QueueInput);
            this.FindControl<TextBlock>("DragGhostPosition")!.Text = dragSong.PositionText;
            this.FindControl<TextBlock>("DragGhostTitle")!.Text = dragSong.DisplayTitle;
            this.FindControl<TextBlock>("DragGhostArtist")!.Text = dragSong.DisplayArtist;
            this.FindControl<TextBlock>("DragGhostDuration")!.Text = dragSong.DurationText;
            DragPreview.Width = Math.Max(120, QueueInput.Bounds.Width - 16);
            DragPreview.IsVisible = true;
        }
        Canvas.SetLeft(DragPreview, 8);
        Canvas.SetTop(DragPreview, Math.Clamp(point.Y - 28, 0, Math.Max(0, QueueInput.Bounds.Height - 56)));
        SetQueueDropTarget(FindQueueRow(point), point);
        e.Handled = true;
    }
    private Border? FindQueueRow(Point point)
    {
        if (QueueInput.InputHitTest(point) is not Visual visual) return null;
        return visual.GetVisualAncestors().Prepend(visual).OfType<Border>()
            .FirstOrDefault(border => border.Classes.Contains("queue-row"));
    }
    private void SetQueueDropTarget(Border? target, Point point)
    {
        if (dropTarget is not null) ClearQueueDropMark(dropTarget);
        dropTarget = target?.DataContext is MpdSong song && song.Id != dragSong?.Id ? target : null;
        if (dropTarget is null) return;
        var top = dropTarget.TranslatePoint(new Point(0, 0), QueueInput)?.Y ?? 0;
        dropAfter = point.Y >= top + dropTarget.Bounds.Height / 2;
        dropTarget.Classes.Add(dropAfter ? "queue-drop-after" : "queue-drop-before");
    }
    private async void Queue_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.Pointer != dragPointer) return;
        var source = draggingQueueSong ? dragSong : null;
        var target = dropTarget?.DataContext as MpdSong;
        var insertAfter = dropAfter;
        ClearQueueDrag();
        if (source is null || target is null) return;
        e.Handled = true;
        try
        {
            var queue = (await client.GetQueueAsync()).ToList();
            var sourceIndex = queue.FindIndex(song => song.Id == source.Id);
            var targetIndex = queue.FindIndex(song => song.Id == target.Id);
            if (sourceIndex < 0 || targetIndex < 0)
                throw new IOException("The queue changed while dragging. Please try again.");
            var insertIndex = targetIndex + (insertAfter ? 1 : 0);
            var destination = insertIndex > sourceIndex ? insertIndex - 1 : insertIndex;
            if (destination != sourceIndex)
                await ActAsync(() => client.MoveIdAsync(source.Id, destination), $"Moved {source.DisplayTitle}");
        }
        catch (Exception error) { Message = error.Message; }
    }
    private void Queue_PointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (e.Pointer == dragPointer && e.Pointer.Captured != QueueInput) ClearQueueDrag();
    }
    private void ClearQueueDrag()
    {
        if (dropTarget is not null) ClearQueueDropMark(dropTarget);
        dragSource?.Classes.Remove("queue-dragging");
        DragPreview.IsVisible = false;
        var pointer = dragPointer;
        dropTarget = null;
        dragSource = null;
        dragSong = null;
        dragPointer = null;
        draggingQueueSong = false;
        if (pointer?.Captured == QueueInput) pointer.Capture(null);
    }
    private static void ClearQueueDropMark(Border border)
    {
        border.Classes.Remove("queue-drop-before");
        border.Classes.Remove("queue-drop-after");
    }
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
