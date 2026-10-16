using CommunityToolkit.Mvvm.ComponentModel;
using StdMachine.Core;
using StdMachine.Hmi.Services;

namespace StdMachine.Hmi.ViewModels;

/// <summary>Base des vues-modèles de page : abonnements à l'instantané libérés quand on quitte la page.</summary>
public abstract class ViewModelBase : ObservableObject, IDisposable
{
    private readonly List<EventHandler<PlcSnapshot>> _snapshotHandlers = new List<EventHandler<PlcSnapshot>>();

    /// <summary>S'abonne aux instantanés (au fil de l'interface, 10 Hz au plus) jusqu'à <see cref="Dispose"/>.</summary>
    protected void OnSnapshots(EventHandler<PlcSnapshot> handler)
    {
        _snapshotHandlers.Add(handler);
        AppServices.Snapshots.Updated += handler;
    }

    public virtual void Dispose()
    {
        foreach (var h in _snapshotHandlers) AppServices.Snapshots.Updated -= h;
        _snapshotHandlers.Clear();
    }
}
