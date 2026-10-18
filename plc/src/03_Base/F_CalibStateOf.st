// Nombre → état de calibration (restauration) ; une calibration en cours à la sauvegarde revient « jamais faite »
FUNCTION F_CalibStateOf : E_CalibState
VAR_INPUT
    fValue : LREAL;
END_VAR
IF fValue > 2.5 AND fValue < 3.5 THEN
    F_CalibStateOf := E_CalibState.Failed;
ELSIF fValue > 1.5 AND fValue < 2.5 THEN
    F_CalibStateOf := E_CalibState.Expired;
ELSIF fValue > 0.5 AND fValue < 1.5 THEN
    F_CalibStateOf := E_CalibState.Valid;
ELSE
    F_CalibStateOf := E_CalibState.Never;
END_IF
END_FUNCTION
