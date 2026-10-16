namespace StdMachine.Core;

/// <summary>Les 8 états du modèle PackML réduit (docs/02 § 8).</summary>
public enum PackMlState
{
    Stopped,
    Resetting,
    Idle,
    Execute,
    Held,
    Stopping,
    Aborted,
    Clearing,
}

/// <summary>Les 6 commandes du modèle PackML réduit.</summary>
public enum StateCommand
{
    Reset,
    Start,
    Stop,
    Hold,
    Abort,
    Clear,
}

/// <summary>Modes d'exécution de la machine.</summary>
public enum MachineMode
{
    Production,
    Maintenance,
}

public static class PackMlRules
{
    /// <summary>États depuis lesquels une commande est acceptée (docs/02 § 8).</summary>
    public static bool IsAccepted(StateCommand command, PackMlState state)
    {
        switch (command)
        {
            case StateCommand.Reset: return state == PackMlState.Stopped;
            case StateCommand.Start: return state == PackMlState.Idle || state == PackMlState.Held;
            case StateCommand.Hold: return state == PackMlState.Execute;
            case StateCommand.Stop:
                return state == PackMlState.Resetting || state == PackMlState.Idle
                    || state == PackMlState.Execute || state == PackMlState.Held;
            case StateCommand.Abort: return state != PackMlState.Aborted;
            case StateCommand.Clear: return state == PackMlState.Aborted;
            default: return false;
        }
    }
}
