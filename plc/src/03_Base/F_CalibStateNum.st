// État de calibration ↔ nombre, pour la sauvegarde des valeurs par la HMI (ST_RetainedItem.aValue[1])
FUNCTION F_CalibStateNum : LREAL
VAR_INPUT
    eState : E_CalibState;
END_VAR
CASE eState OF
    E_CalibState.Valid: F_CalibStateNum := 1;
    E_CalibState.Expired: F_CalibStateNum := 2;
    E_CalibState.Failed: F_CalibStateNum := 3;
    E_CalibState.Running: F_CalibStateNum := 4;
ELSE
    F_CalibStateNum := 0;
END_CASE
END_FUNCTION
