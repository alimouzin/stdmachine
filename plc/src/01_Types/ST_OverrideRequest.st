// Override d'un nœud (docs/06 § 6.2) ; nStation 0 = machine ; nObject 0 = la station ou la machine
{attribute 'pack_mode' := '1'}
TYPE ST_OverrideRequest :
STRUCT
    nSeq : UDINT;
    nStation : USINT;
    nObject : UINT;
    fValue : LREAL;                 // 0..100 %
    bManual : BOOL;                 // override manuel de l'axe
    sUser : STRING(24);
END_STRUCT
END_TYPE
