// Déclaration d'une calibration d'une station (docs/05 § 10.1)
{attribute 'pack_mode' := '1'}
TYPE ST_CalibDef :
STRUCT
    nTable : USINT;             // table de type Calibration
    nId : UDINT;                // identifiant stable
    nValidityDays : UINT;       // 0 = n'expire jamais
END_STRUCT
END_TYPE
