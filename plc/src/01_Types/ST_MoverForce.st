// Forçage d'un mover (docs/12 § 4)
{attribute 'pack_mode' := '1'}
TYPE ST_MoverForce :
STRUCT
    bOutOfService : BOOL;
    bImmediate : BOOL;
    nReason : USINT;
    tSince : LINT;
    sBy : STRING(24);
END_STRUCT
END_TYPE
