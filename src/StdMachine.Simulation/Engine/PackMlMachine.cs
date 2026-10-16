using StdMachine.Core;

namespace StdMachine.Simulation.Engine;

/// <summary>Machine d'états PackML réduite (docs/05 § 2), identique pour la machine et pour chaque station.</summary>
public sealed class PackMlMachine
{
    public PackMlState State { get; private set; } = PackMlState.Stopped;
    public bool Entry { get; private set; }
    public bool LastCommandAccepted { get; private set; } = true;

    /// <summary>Un cycle. <paramref name="command"/> : commande reçue ce cycle (ou null).</summary>
    public void Cycle(StateCommand? command, bool stateComplete, bool fault)
    {
        Entry = false;
        var next = State;
        if (fault && State != PackMlState.Aborted) next = PackMlState.Aborted;
        else
        {
            switch (State)
            {
                case PackMlState.Stopped:
                    if (command == StateCommand.Reset) next = PackMlState.Resetting;
                    else if (command == StateCommand.Abort) next = PackMlState.Aborted;
                    break;
                case PackMlState.Resetting:
                    if (command == StateCommand.Abort) next = PackMlState.Aborted;
                    else if (command == StateCommand.Stop) next = PackMlState.Stopping;
                    else if (stateComplete) next = PackMlState.Idle;
                    break;
                case PackMlState.Idle:
                    if (command == StateCommand.Start) next = PackMlState.Execute;
                    else if (command == StateCommand.Stop) next = PackMlState.Stopping;
                    else if (command == StateCommand.Abort) next = PackMlState.Aborted;
                    break;
                case PackMlState.Execute:
                    if (command == StateCommand.Hold) next = PackMlState.Held;
                    else if (command == StateCommand.Stop) next = PackMlState.Stopping;
                    else if (command == StateCommand.Abort) next = PackMlState.Aborted;
                    break;
                case PackMlState.Held:
                    if (command == StateCommand.Start) next = PackMlState.Execute;
                    else if (command == StateCommand.Stop) next = PackMlState.Stopping;
                    else if (command == StateCommand.Abort) next = PackMlState.Aborted;
                    break;
                case PackMlState.Stopping:
                    if (command == StateCommand.Abort) next = PackMlState.Aborted;
                    else if (stateComplete) next = PackMlState.Stopped;
                    break;
                case PackMlState.Aborted:
                    if (command == StateCommand.Clear) next = PackMlState.Clearing;
                    break;
                case PackMlState.Clearing:
                    if (command == StateCommand.Abort) next = PackMlState.Aborted;
                    else if (stateComplete) next = PackMlState.Stopped;
                    break;
            }
        }
        LastCommandAccepted = command == null || next != State;
        if (next != State) { State = next; Entry = true; }
    }

    /// <summary>Force un état (chargement d'un déploiement, remise à zéro du simulateur).</summary>
    public void Reset(PackMlState state = PackMlState.Stopped) { State = state; Entry = false; LastCommandAccepted = true; }
}
