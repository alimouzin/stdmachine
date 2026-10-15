using System.Windows;
using System.Windows.Input;
using Core.Plc;
using Hmi.Mvvm;

namespace Hmi.Services;

/// <summary>
/// Dernier état connu du PLC, republié sur le thread UI. Les pages s'y abonnent
/// au lieu de parler directement au client PLC pour la lecture.
/// </summary>
public sealed class PlcMonitor : ViewModelBase
{
    private MachineStatus _status;

    public PlcMonitor(IPlcClient plc)
    {
        Plc = plc;
        _status = plc.GetStatus();
        plc.StatusChanged += () => Post(Refresh);
    }

    public IPlcClient Plc { get; }

    public MachineStatus Status
    {
        get => _status;
        private set => Set(ref _status, value);
    }

    /// <summary>Levé sur le thread UI après chaque mise à jour de <see cref="Status"/>.</summary>
    public event Action? Updated;

    private void Refresh()
    {
        Status = Plc.GetStatus();
        Updated?.Invoke();
        CommandManager.InvalidateRequerySuggested();
    }

    private static void Post(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess()) action();
        else dispatcher.BeginInvoke(action);
    }
}
