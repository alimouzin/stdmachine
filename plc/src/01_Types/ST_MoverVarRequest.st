// Écrire ou remettre à zéro une variable de mover (docs/05 § 5.8)
{attribute 'pack_mode' := '1'}
TYPE ST_MoverVarRequest :
STRUCT
    nSeq : UDINT;
    nMover : USINT;
    nVar : USINT;                   // rang
    bReset : BOOL;                  // valeur initiale
    fValue : LREAL;
    stPos : ST_Position;
    sUser : STRING(24);
END_STRUCT
END_TYPE
