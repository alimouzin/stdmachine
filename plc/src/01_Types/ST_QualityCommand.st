// Libérer ou remettre à zéro une portée qualité (docs/10 § 10)
{attribute 'pack_mode' := '1'}
TYPE ST_QualityCommand :
STRUCT
    nSeq : UDINT;
    eCmd : E_QualityCmd;
    eScope : E_QualityScope;
    nIndex : USINT;
    nReason : USINT;
    sUser : STRING(24);
END_STRUCT
END_TYPE
