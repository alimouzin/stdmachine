using System.Windows;
using System.Windows.Controls;
using StdMachine.Hmi.Infrastructure;
using StdMachine.Hmi.Services;

namespace StdMachine.Hmi.Controls;

/// <summary>Connexion (docs/08 § 6.14) : tuiles des utilisateurs récents et pavé PIN. Maquette : tout PIN de 4 chiffres est accepté.</summary>
public partial class LoginPanel : UserControl
{
    public static readonly System.Windows.Data.IValueConverter Initials = new FuncConverter((v, _) =>
    {
        var s = (v as string ?? "").Replace(".", " ").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        return s.Length == 0 ? "?" : s.Length == 1 ? s[0].Substring(0, 1) : s[0].Substring(0, 1) + s[1].Substring(0, 1);
    });

    private UserInfo? _selected;
    private string _pin = string.Empty;

    public LoginPanel()
    {
        InitializeComponent();
        UserList.ItemsSource = SessionService.Users;
    }

    public event EventHandler? Closed;

    private void User_Click(object sender, RoutedEventArgs e)
    {
        _selected = (UserInfo)((Button)sender).Tag;
        WhoText.Text = _selected.Name + " · " + _selected.LevelName;
        _pin = string.Empty;
        PinError.Text = string.Empty;
        Refresh();
    }

    private void Digit_Click(object sender, RoutedEventArgs e)
    {
        if (_pin.Length < 4) _pin += (string)((Button)sender).Tag;
        PinError.Text = string.Empty;
        Refresh();
        if (_pin.Length == 4) Validate();
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (_pin.Length > 0) _pin = _pin.Substring(0, _pin.Length - 1);
        Refresh();
    }

    private void Ok_Click(object sender, RoutedEventArgs e) => Validate();

    private void Validate()
    {
        if (_selected == null) { PinError.Text = "Choisissez d'abord un utilisateur."; return; }
        if (_pin.Length < 4) { PinError.Text = "Le code comporte 4 chiffres."; return; }
        AppServices.Session.Login(_selected);
        AppServices.Toast.Show("Connecté : " + _selected.Name + " (" + _selected.LevelName + ")", ToastKind.Success);
        Closed?.Invoke(this, EventArgs.Empty);
    }

    private void Operator_Click(object sender, RoutedEventArgs e)
    {
        AppServices.Session.Logout();
        Closed?.Invoke(this, EventArgs.Empty);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Closed?.Invoke(this, EventArgs.Empty);

    private void Refresh() => PinText.Text = string.Join(" ", Enumerable.Range(0, 4).Select(i => i < _pin.Length ? "●" : "○"));
}
