// Écriture d'une sortie numérique par référence (docs/04 § 6.1) : référence invalide -> ignorée
FUNCTION F_WriteDO : BOOL
VAR_INPUT
    stRef : ST_SignalRef;
    bValue : BOOL;
END_VAR
IF stRef.eKind <> E_SignalKind.DO OR GVL_IoRef.pDO = 0 OR stRef.nIndex < 0 OR stRef.nIndex >= GVL_Const.MAX_DO THEN
    F_WriteDO := FALSE;
    RETURN;
END_IF
GVL_IoRef.pDO^[stRef.nIndex] := bValue XOR stRef.bInvert;
F_WriteDO := TRUE;
