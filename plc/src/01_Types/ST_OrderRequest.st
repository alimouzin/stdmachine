// OF : activer, suspendre, annuler (docs/05 § 5.6)
{attribute 'pack_mode' := '1'}
TYPE ST_OrderRequest :
STRUCT
    nSeq : UDINT;
    eCmd : E_OrderCmd;
    nOrder : UDINT;                 // n° interne
    sNumber : STRING(40);
    sReference : STRING(40);
    nQuantity : UDINT;
    nGoodDone : UDINT;              // compteurs déjà faits (reprise)
    nBadDone : UDINT;
    aParam : ARRAY[1..GVL_Const.MAX_ORDER_PARAMS] OF LREAL;
    sUser : STRING(24);
END_STRUCT
END_TYPE
