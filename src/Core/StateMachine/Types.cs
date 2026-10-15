namespace Core.StateMachine;

/// <summary>PackML réduit : 8 états.</summary>
public enum SeqState { Stopped, Resetting, Idle, Execute, Held, Stopping, Aborted, Clearing }

/// <summary>PackML réduit : 6 commandes.</summary>
public enum SeqCommand { Reset, Start, Stop, Hold, Abort, Clear }

/// <summary>PackML réduit : 2 modes.</summary>
public enum SeqMode { Production, Maintenance }
