// Lecture d'une entrée numérique par référence (docs/04 § 6.1) : référence invalide -> FALSE
FUNCTION F_ReadDI : BOOL
VAR_INPUT
    stRef : ST_SignalRef;
END_VAR
IF stRef.eKind <> E_SignalKind.DI OR GVL_IoRef.pDI = 0 OR stRef.nIndex < 0 OR stRef.nIndex >= GVL_Const.MAX_DI THEN
    F_ReadDI := FALSE;
    RETURN;
END_IF
F_ReadDI := GVL_IoRef.pDI^[stRef.nIndex] XOR stRef.bInvert;
