// Forçage d'une station (docs/12 § 3)
{attribute 'pack_mode' := '1'}
TYPE ST_StationForce :
STRUCT
    bOutOfProduction : BOOL;
    nReason : USINT;
    tSince : LINT;
    sBy : STRING(24);
END_STRUCT
END_TYPE
