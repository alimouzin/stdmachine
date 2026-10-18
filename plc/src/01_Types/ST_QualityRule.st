// Réglage d'une portée qualité (docs/10 § 9)
{attribute 'pack_mode' := '1'}
TYPE ST_QualityRule :
STRUCT
    nConsecWarn : USINT;        // 0 = désactivé
    nConsecAction : USINT;
    nWindow : USINT;            // 1..255
    nMinSample : USINT;
    fRateWarn : REAL;           // %, 0 = désactivé
    fRateAction : REAL;
    eReactionConsec : E_QualityReaction;
    eReactionRate : E_QualityReaction;
END_STRUCT
END_TYPE
