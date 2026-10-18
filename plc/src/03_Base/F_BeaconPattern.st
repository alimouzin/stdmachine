// Couleurs de la balise selon l'état (docs/02 § 8, tableau des balises)
FUNCTION F_BeaconPattern : UINT
VAR_INPUT
    eState : E_State;
    bMaintenance : BOOL;         // mode Maintenance, ou état déclaré maintenance, essais, nettoyage
    bOperatorAwaited : BOOL;     // mover à vider, fin d'OF, machine vidée, réaction qualité…
    bStationAborted : BOOL;      // machine : une station Aborted pendant que la machine produit
END_VAR
VAR
    n : UINT;
END_VAR
CASE eState OF
    E_State.Execute:
        n := GVL_BeaconBits.GREEN;
        IF bStationAborted THEN n := n OR GVL_BeaconBits.RED_BLINK; END_IF
    E_State.Idle:
        n := GVL_BeaconBits.GREEN_BLINK;
    E_State.Held, E_State.Stopped:
        n := GVL_BeaconBits.ORANGE;
    E_State.Resetting, E_State.Stopping, E_State.Clearing:
        n := GVL_BeaconBits.ORANGE_BLINK;
    E_State.Aborted:
        n := GVL_BeaconBits.RED_BLINK OR GVL_BeaconBits.BUZZER;
END_CASE
IF bMaintenance THEN n := n OR GVL_BeaconBits.BLUE; END_IF
IF bOperatorAwaited THEN n := n OR GVL_BeaconBits.BLUE_BLINK; END_IF
F_BeaconPattern := n;
