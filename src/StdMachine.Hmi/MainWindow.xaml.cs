using System.Windows;
using StdMachine.Hmi.Controls;
using StdMachine.Hmi.Infrastructure;
using StdMachine.Hmi.Services;
using StdMachine.Hmi.ViewModels;

namespace StdMachine.Hmi;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new ShellViewModel();
        TouchSettings.Apply(this);
    }

    private void Login_Click(object sender, RoutedEventArgs e) => ShowLogin();

    /// <summary>Connexion en surimpression (docs/08 § 6.14).</summary>
    public void ShowLogin()
    {
        var host = OverlayHost.Current;
        if (host == null) return;
        var panel = new LoginPanel();
        var handle = host.Show(panel, OverlayPlacement.Center, dim: true);
        panel.Closed += (_, _) => handle.Dispose();
    }
}
