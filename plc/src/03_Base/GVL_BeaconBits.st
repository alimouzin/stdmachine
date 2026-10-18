// Codage de la balise (ST_MachineStatus.nBeacon, ST_StationStatus.nBeacon, I_Beacon.SetPattern)
{attribute 'qualified_only'}
VAR_GLOBAL CONSTANT
    RED : UINT := 16#0001;
    ORANGE : UINT := 16#0002;
    GREEN : UINT := 16#0004;
    BLUE : UINT := 16#0008;
    RED_BLINK : UINT := 16#0100;
    ORANGE_BLINK : UINT := 16#0200;
    GREEN_BLINK : UINT := 16#0400;
    BLUE_BLINK : UINT := 16#0800;
    BUZZER : UINT := 16#1000;
END_VAR
