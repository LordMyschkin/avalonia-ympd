using Avalonia.Controls;
using Avalonia.Interactivity;

namespace ModernYmpd;

public enum ExitChoice { Cancel, KeepRunning, Quit }

public partial class ExitDialog : Window
{
    public ExitDialog() : this(false) { }

    public ExitDialog(bool autoFillEnabled)
    {
        InitializeComponent();
        this.FindControl<TextBlock>("FillStatus")!.Text = autoFillEnabled
            ? "Die Playlist wird weiter automatisch aufgefüllt, solange die App läuft."
            : "Die automatische Playlist-Auffüllung ist derzeit ausgeschaltet.";
        if (autoFillEnabled)
            this.FindControl<Button>("KeepRunningButton")!.Content = "Playlist weiter befüllen";
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(ExitChoice.Cancel);
    private void KeepRunning_Click(object? sender, RoutedEventArgs e) => Close(ExitChoice.KeepRunning);
    private void Quit_Click(object? sender, RoutedEventArgs e) => Close(ExitChoice.Quit);
}
