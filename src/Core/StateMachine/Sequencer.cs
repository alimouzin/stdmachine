namespace Core.StateMachine;

/// <summary>
/// Modèle d'états PackML réduit d'un séquenceur (général ou de station).
/// Les états transitoires (Resetting, Stopping, Clearing) se terminent par <see cref="Complete"/>.
/// </summary>
public sealed class Sequencer
{
    private static readonly Dictionary<(SeqState, SeqCommand), SeqState> Transitions = new()
    {
        [(SeqState.Stopped, SeqCommand.Reset)] = SeqState.Resetting,
        [(SeqState.Idle, SeqCommand.Start)] = SeqState.Execute,
        [(SeqState.Held, SeqCommand.Start)] = SeqState.Execute,
        [(SeqState.Execute, SeqCommand.Hold)] = SeqState.Held,
        [(SeqState.Idle, SeqCommand.Stop)] = SeqState.Stopping,
        [(SeqState.Execute, SeqCommand.Stop)] = SeqState.Stopping,
        [(SeqState.Held, SeqCommand.Stop)] = SeqState.Stopping,
        [(SeqState.Idle, SeqCommand.Abort)] = SeqState.Aborted,
        [(SeqState.Execute, SeqCommand.Abort)] = SeqState.Aborted,
        [(SeqState.Held, SeqCommand.Abort)] = SeqState.Aborted,
        [(SeqState.Stopping, SeqCommand.Abort)] = SeqState.Aborted,
        [(SeqState.Resetting, SeqCommand.Abort)] = SeqState.Aborted,
        [(SeqState.Aborted, SeqCommand.Clear)] = SeqState.Clearing,
    };

    public SeqState State { get; private set; } = SeqState.Stopped;
    public SeqMode Mode { get; private set; } = SeqMode.Production;

    public event Action? Changed;

    /// <summary>En Maintenance, Reset et Start sont refusés : pas de production.</summary>
    public bool CanApply(SeqCommand command)
    {
        if (Mode == SeqMode.Maintenance && command is SeqCommand.Reset or SeqCommand.Start) return false;
        return Transitions.ContainsKey((State, command));
    }

    public bool Apply(SeqCommand command)
    {
        if (!CanApply(command)) return false;
        State = Transitions[(State, command)];
        Changed?.Invoke();
        return true;
    }

    /// <summary>Termine un état transitoire : Resetting→Idle, Stopping→Stopped, Clearing→Stopped.</summary>
    public bool Complete()
    {
        var next = State switch
        {
            SeqState.Resetting => SeqState.Idle,
            SeqState.Stopping => SeqState.Stopped,
            SeqState.Clearing => SeqState.Stopped,
            _ => (SeqState?)null,
        };
        if (next is null) return false;
        State = next.Value;
        Changed?.Invoke();
        return true;
    }

    /// <summary>Le mode ne change qu'à l'arrêt.</summary>
    public bool TrySetMode(SeqMode mode)
    {
        if (State != SeqState.Stopped) return false;
        if (Mode == mode) return true;
        Mode = mode;
        Changed?.Invoke();
        return true;
    }
}
