// OF dans la table du séquenceur général (docs/05 § 5.6)
{attribute 'pack_mode' := '1'}
TYPE ST_OrderState :
STRUCT
    nOrder : UDINT;                 // n° interne, 0 = libre
    eState : E_OrderState;
    sNumber : STRING(40);
    sReference : STRING(40);
    nQuantity : UDINT;
    nGood : UDINT;
    nBad : UDINT;
    nInProcess : UDINT;
    aParam : ARRAY[1..GVL_Const.MAX_ORDER_PARAMS] OF LREAL;
    stQuality : ST_QualityTrack;    // budget de rebuts de l'OF (docs/10 § 4.3)
END_STRUCT
END_TYPE
