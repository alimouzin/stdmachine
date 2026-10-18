// Demande du canal manuel (docs/05 § 7)
{attribute 'pack_mode' := '1'}
TYPE ST_ManualRequest :
STRUCT
    nSeq : UDINT;
    eKind : E_ManualKind;
    nObject : UINT;                 // n° local
    nAction : UINT;
    stParams : ST_ActParams;        // résolus par la HMI
    nTeachPos : INT;                // position nommée à apprendre
    nTeachMask : USINT;             // bits X, Y, Z, C
    aTeachFrom : ARRAY[1..4] OF UINT;   // objets qui fournissent X, Y, Z, C
    sUser : STRING(24);
END_STRUCT
END_TYPE
