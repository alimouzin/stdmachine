// Lecture brute d'une entrée analogique (docs/04 § 6.1) : référence invalide -> 0
FUNCTION F_ReadAI : INT
VAR_INPUT
    stRef : ST_SignalRef;
END_VAR
IF stRef.eKind <> E_SignalKind.AI OR GVL_IoRef.pAI = 0 OR stRef.nIndex < 0 OR stRef.nIndex >= GVL_Const.MAX_AI THEN
    F_ReadAI := 0;
    RETURN;
END_IF
F_ReadAI := GVL_IoRef.pAI^[stRef.nIndex];
