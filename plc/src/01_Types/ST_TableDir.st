// Répertoire des tables (docs/06 § 2)
{attribute 'pack_mode' := '1'}
TYPE ST_TableDir :
STRUCT
    bUsed : BOOL;
    sName : STRING(40);
    eKind : E_TableKind;
    nStart : INT;               // index de la première instruction dans aInstr
    nLength : INT;
END_STRUCT
END_TYPE
