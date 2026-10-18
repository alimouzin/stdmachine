// État d'une instance de portée qualité (docs/10 § 9)
{attribute 'pack_mode' := '1'}
TYPE ST_QualityTrack :
STRUCT
    nConsec : USINT;
    aWindow : ARRAY[0..31] OF BYTE;     // 256 résultats en bits (1 = mauvais)
    nHead : USINT;
    nCount : USINT;
    nBadInWindow : USINT;
    nProbationLeft : USINT;
    eLevel : E_QualityLevel;
    bLocked : BOOL;
    nTotalGood : UDINT;
    nTotalBad : UDINT;
END_STRUCT
END_TYPE
