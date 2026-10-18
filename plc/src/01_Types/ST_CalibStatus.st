// État d'une calibration (docs/05 § 10.1)
{attribute 'pack_mode' := '1'}
TYPE ST_CalibStatus :
STRUCT
    nId : UDINT;
    eState : E_CalibState;
    tLast : LINT;
    sUser : STRING(24);
    nValidityDays : UINT;           // 0 = n'expire jamais
END_STRUCT
END_TYPE
