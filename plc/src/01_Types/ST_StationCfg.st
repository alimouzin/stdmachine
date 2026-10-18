// Station dans la configuration machine (docs/06 § 4)
{attribute 'pack_mode' := '1'}
TYPE ST_StationCfg :
STRUCT
    bUsed : BOOL;
    sName : STRING(40);
    nGroupMask : UINT;          // bit n-1 = fonction n
    nWorkPos : INT;             // index dans aPlanarPos
    aQueuePos : ARRAY[1..GVL_Const.MAX_QUEUE] OF INT;   // 0 = absente
    fOverride : LREAL;
END_STRUCT
END_TYPE
