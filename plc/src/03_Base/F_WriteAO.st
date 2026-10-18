// Écriture brute d'une sortie analogique (docs/04 § 6.1) : référence invalide -> ignorée
FUNCTION F_WriteAO : BOOL
VAR_INPUT
    stRef : ST_SignalRef;
    nValue : INT;
END_VAR
IF stRef.eKind <> E_SignalKind.AO OR GVL_IoRef.pAO = 0 OR stRef.nIndex < 0 OR stRef.nIndex >= GVL_Const.MAX_AO THEN
    F_WriteAO := FALSE;
    RETURN;
END_IF
GVL_IoRef.pAO^[stRef.nIndex] := nValue;
F_WriteAO := TRUE;
