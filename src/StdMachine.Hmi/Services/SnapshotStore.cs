using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using StdMachine.Core;

namespace StdMachine.Hmi.Services;

/// <summary>
/// Reçoit les instantanés du PLC (sur un fil quelconque), garde le plus récent et le publie au fil de l'interface
/// à 10 Hz au plus (docs/07 § 6). Les vues et vues-modèles ne lisent l'état que par ici.
/// </summary>
public sealed class SnapshotStore : ObservableObject
{
    private readonly IPlcClient _client;
    private readonly Dispatcher _dispatcher;
    private PlcSnapshot _current;
    private PlcSnapshot? _pending;
    private bool _scheduled;

    public SnapshotStore(IPlcClient client, Dispatcher dispatcher)
    {
        _client = client;
        _dispatcher = dispatcher;
        _current = client.Snapshot;
        client.SnapshotUpdated += OnSnapshot;
    }

    public IPlcClient Client => _client;

    public PlcSnapshot Current => _current;

    /// <summary>Levé au fil de l'interface à chaque nouvel instantané.</summary>
    public event EventHandler<PlcSnapshot>? Updated;

    /// <summary>Force un instantané (captures d'écran : scénario figé à un instant donné).</summary>
    public void Freeze(PlcSnapshot snapshot)
    {
        _client.SnapshotUpdated -= OnSnapshot;
        Apply(snapshot);
    }

    private void OnSnapshot(object? sender, PlcSnapshot snapshot)
    {
        lock (this)
        {
            _pending = snapshot;
            if (_scheduled) return;
            _scheduled = true;
        }
        _dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(Flush));
    }

    private void Flush()
    {
        PlcSnapshot? s;
        lock (this)
        {
            s = _pending;
            _pending = null;
            _scheduled = false;
        }
        if (s != null) Apply(s);
    }

    private void Apply(PlcSnapshot s)
    {
        _current = s;
        OnPropertyChanged(nameof(Current));
        Updated?.Invoke(this, s);
    }
}
