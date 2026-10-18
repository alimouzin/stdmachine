// Forçages actifs (docs/12 § 7), persistants
{attribute 'pack_mode' := '1'}
TYPE ST_MachineForce :
STRUCT
    eDeclared : E_DeclaredState;
    bStartBlocked : BOOL;
    nReason : USINT;
    tSince : LINT;
    tUntil : LINT;
    sBy : STRING(24);
    aStation : ARRAY[1..GVL_Const.MAX_STATIONS] OF ST_StationForce;
    aMover : ARRAY[1..GVL_Const.MAX_MOVERS] OF ST_MoverForce;
END_STRUCT
END_TYPE
