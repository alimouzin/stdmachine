// Maintenance : lancer une table d'une station (docs/06 § 6.2)
{attribute 'pack_mode' := '1'}
TYPE ST_LaunchRequest :
STRUCT
    nSeq : UDINT;
    nStation : USINT;               // 0 = calibration machine
    nTable : USINT;
    nMover : USINT;                 // 0 = sans mover
    nSlot : USINT;
    aParam : ARRAY[1..GVL_Const.MAX_PARAMS] OF LREAL;
    sUser : STRING(24);
END_STRUCT
END_TYPE
