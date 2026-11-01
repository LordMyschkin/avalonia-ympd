using System.Reflection;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace ModernYmpd;

public partial class AboutDialog : Window
{
    public AboutDialog()
    {
        InitializeComponent();
        var version = typeof(App).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion.Split('+')[0]
            ?? typeof(App).Assembly.GetName().Version?.ToString(3)
            ?? "unknown";
        this.FindControl<TextBlock>("VersionText")!.Text = $"Version {version}";
    }

    private void Close_Click(object? sender, RoutedEventArgs e) => Close();
}
