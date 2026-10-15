using Core.Access;
using Hmi.Mvvm;
using Hmi.Services;

namespace Hmi.ViewModels;

/// <summary>Connexion : choix de l'utilisateur puis PIN au pavé numérique.</summary>
public sealed class LoginViewModel : ViewModelBase
{
    private const int MaxPinLength = 8;

    private readonly SessionService _session;
    private User? _selectedUser;
    private string _pin = "";
    private string _error = "";

    public LoginViewModel(SessionService session, Action close)
    {
        _session = session;
        DigitCommand = new RelayCommand(p =>
        {
            if (p is string d && _pin.Length < MaxPinLength) Pin += d;
        });
        BackspaceCommand = new RelayCommand(() => { if (_pin.Length > 0) Pin = _pin[..^1]; });
        ConfirmCommand = new RelayCommand(() =>
        {
            if (_selectedUser is not null && _session.Login(_selectedUser.Name, _pin))
            {
                close();
                return;
            }
            Error = "PIN incorrect.";
            Pin = "";
        }, () => _selectedUser is not null && _pin.Length > 0);
        CancelCommand = new RelayCommand(close);
    }

    public IReadOnlyList<User> Users => _session.Policy.Users;

    public User? SelectedUser
    {
        get => _selectedUser;
        set { if (Set(ref _selectedUser, value)) { Pin = ""; Error = ""; } }
    }

    public string Pin
    {
        get => _pin;
        private set { if (Set(ref _pin, value)) OnPropertyChanged(nameof(PinDisplay)); }
    }

    public string PinDisplay => new('●', _pin.Length);

    public string Error
    {
        get => _error;
        private set => Set(ref _error, value);
    }

    public RelayCommand DigitCommand { get; }
    public RelayCommand BackspaceCommand { get; }
    public RelayCommand ConfirmCommand { get; }
    public RelayCommand CancelCommand { get; }

    /// <summary>Remet le formulaire à zéro avant chaque ouverture.</summary>
    public void Reset()
    {
        OnPropertyChanged(nameof(Users));
        SelectedUser = null;
        Pin = "";
        Error = "";
    }
}
