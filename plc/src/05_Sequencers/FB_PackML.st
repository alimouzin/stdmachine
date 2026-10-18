// Machine d'états PackML réduite, commune à la machine et aux stations (docs/05 § 2)
FUNCTION_BLOCK FB_PackML
VAR_INPUT
    eCmd : E_Cmd;                   // commande du cycle, None sinon
    bStateComplete : BOOL;          // le travail de l'état est fini
    bFault : BOOL;                  // défaut qui impose Aborted
END_VAR
VAR_OUTPUT
    eState : E_State := E_State.Stopped;
    bEntry : BOOL;                  // vrai au premier cycle dans un nouvel état
    eLastCmdResult : E_CmdResult;   // None si aucune commande ce cycle
    tSince : LINT;                  // heure d'entrée dans l'état (UTC)
END_VAR
VAR
    eNext : E_State;
END_VAR

bEntry := FALSE;
eNext := eState;
IF bFault AND eState <> E_State.Aborted THEN
    eNext := E_State.Aborted;
ELSE
    CASE eState OF
    E_State.Stopped:
        IF eCmd = E_Cmd.Reset THEN eNext := E_State.Resetting;
        ELSIF eCmd = E_Cmd.Abort THEN eNext := E_State.Aborted; END_IF
    E_State.Resetting:
        IF eCmd = E_Cmd.Abort THEN eNext := E_State.Aborted;
        ELSIF eCmd = E_Cmd.Stop THEN eNext := E_State.Stopping;
        ELSIF bStateComplete THEN eNext := E_State.Idle; END_IF
    E_State.Idle:
        IF eCmd = E_Cmd.Start THEN eNext := E_State.Execute;
        ELSIF eCmd = E_Cmd.Stop THEN eNext := E_State.Stopping;
        ELSIF eCmd = E_Cmd.Abort THEN eNext := E_State.Aborted; END_IF
    E_State.Execute:
        IF eCmd = E_Cmd.Hold THEN eNext := E_State.Held;
        ELSIF eCmd = E_Cmd.Stop THEN eNext := E_State.Stopping;
        ELSIF eCmd = E_Cmd.Abort THEN eNext := E_State.Aborted; END_IF
    E_State.Held:
        IF eCmd = E_Cmd.Start THEN eNext := E_State.Execute;
        ELSIF eCmd = E_Cmd.Stop THEN eNext := E_State.Stopping;
        ELSIF eCmd = E_Cmd.Abort THEN eNext := E_State.Aborted; END_IF
    E_State.Stopping:
        IF eCmd = E_Cmd.Abort THEN eNext := E_State.Aborted;
        ELSIF bStateComplete THEN eNext := E_State.Stopped; END_IF
    E_State.Aborted:
        IF eCmd = E_Cmd.Clear THEN eNext := E_State.Clearing; END_IF
    E_State.Clearing:
        IF eCmd = E_Cmd.Abort THEN eNext := E_State.Aborted;
        ELSIF bStateComplete THEN eNext := E_State.Stopped; END_IF
    END_CASE
END_IF
IF eCmd = E_Cmd.None OR eCmd = E_Cmd.DrainStart OR eCmd = E_Cmd.DrainCancel THEN
    eLastCmdResult := E_CmdResult.None;         // pas de commande PackML ce cycle
ELSE
    eLastCmdResult := SEL(eNext <> eState, E_CmdResult.Rejected, E_CmdResult.Accepted);
END_IF
IF eNext <> eState THEN
    eState := eNext;
    bEntry := TRUE;
    tSince := F_NowUtc();
END_IF

// Accepterait-on cette commande dans l'état courant ? (pour refuser avant de l'appliquer, sans changer d'état)
METHOD Accepts : BOOL
VAR_INPUT
    eCommand : E_Cmd;
END_VAR
CASE eCommand OF
    E_Cmd.Reset: Accepts := eState = E_State.Stopped;
    E_Cmd.Start: Accepts := eState = E_State.Idle OR eState = E_State.Held;
    E_Cmd.Hold: Accepts := eState = E_State.Execute;
    E_Cmd.Stop: Accepts := eState = E_State.Resetting OR eState = E_State.Idle OR eState = E_State.Execute OR eState = E_State.Held;
    E_Cmd.Abort: Accepts := eState <> E_State.Aborted;
    E_Cmd.Clear: Accepts := eState = E_State.Aborted;
ELSE
    Accepts := FALSE;
END_CASE
END_METHOD

// Remet l'automate à Stopped (déploiement, démarrage du PLC)
METHOD ForceStopped
eState := E_State.Stopped;
bEntry := TRUE;
tSince := F_NowUtc();
END_METHOD
END_FUNCTION_BLOCK
