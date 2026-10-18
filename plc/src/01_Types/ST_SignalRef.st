// Référence vers l'image E/S (docs/04 § 5)
{attribute 'pack_mode' := '1'}
TYPE ST_SignalRef :
STRUCT
    eKind : E_SignalKind;
    nIndex : INT;               // index dans le tableau de GVL_IO correspondant
    bInvert : BOOL;
END_STRUCT
END_TYPE
