// (fonction, opération) → table (docs/06 § 4)
{attribute 'pack_mode' := '1'}
TYPE ST_OpMap :
STRUCT
    nGroup : USINT;
    nOperationNo : USINT;       // 1..8
    nTable : USINT;
    bPrepare : BOOL;            // la table contient WaitMover (docs/05 § 4.1)
END_STRUCT
END_TYPE
