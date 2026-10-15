using System.Windows.Input;
using Core.Access;

namespace Hmi.Services;

/// <summary>Utilisateur connecté et droits courants.</summary>
public sealed class SessionService
{
    public SessionService(AccessPolicy policy)
    {
        Policy = policy;
        policy.Changed += () => Changed?.Invoke();
    }

    public AccessPolicy Policy { get; }

    public User? CurrentUser { get; private set; }

    /// <summary>Levé quand l'utilisateur ou les droits changent.</summary>
    public event Action? Changed;

    public bool CanDo(ActionType action) => Policy.CanDo(CurrentUser, action);

    public bool Login(string name, string pin)
    {
        var user = Policy.Login(name, pin);
        if (user is null) return false;
        CurrentUser = user;
        RaiseChanged();
        return true;
    }

    public void Logout()
    {
        CurrentUser = null;
        RaiseChanged();
    }

    private void RaiseChanged()
    {
        Changed?.Invoke();
        CommandManager.InvalidateRequerySuggested();
    }
}
