using Avalonia.Controls;
using Avalonia.Interactivity;

namespace ModernYmpd;

public enum ExitChoice { Cancel, Quit }

public partial class ExitDialog : Window
{
    private readonly Func<Task> pauseMpd;

    public ExitDialog() : this(() => Task.CompletedTask) { }

    public ExitDialog(Func<Task> pauseMpd)
    {
        this.pauseMpd = pauseMpd;
        InitializeComponent();
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(ExitChoice.Cancel);
    private void Quit_Click(object? sender, RoutedEventArgs e) => Close(ExitChoice.Quit);

    private async void PauseAndQuit_Click(object? sender, RoutedEventArgs e)
    {
        var button = this.FindControl<Button>("PauseButton")!;
        button.IsEnabled = false;
        try
        {
            await pauseMpd();
            Close(ExitChoice.Quit);
        }
        catch (Exception error)
        {
            CrashLog.Write(error, "Pause MPD before exit");
            this.FindControl<TextBlock>("ErrorText")!.Text = error.Message;
            button.IsEnabled = true;
        }
    }
}
