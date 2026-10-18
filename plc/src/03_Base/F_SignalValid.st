// Vrai si la référence désigne un signal du bon type dans l'image E/S
FUNCTION F_SignalValid : BOOL
VAR_INPUT
    stRef : ST_SignalRef;
    eKind : E_SignalKind;
END_VAR
F_SignalValid := stRef.eKind = eKind AND stRef.nIndex >= 0;
