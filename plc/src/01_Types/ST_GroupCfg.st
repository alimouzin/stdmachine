// Fonction (docs/06 § 4)
{attribute 'pack_mode' := '1'}
TYPE ST_GroupCfg :
STRUCT
    bUsed : BOOL;
    sName : STRING(40);
    eRole : E_GroupRole;
    nStationMask : UINT;        // bit n-1 = station n
    nOperations : USINT;
    nPriority : USINT;          // 1..9
END_STRUCT
END_TYPE
