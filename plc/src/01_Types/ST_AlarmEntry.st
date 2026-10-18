// Alarme active (docs/04 § 10, docs/14)
{attribute 'pack_mode' := '1'}
TYPE ST_AlarmEntry :
STRUCT
    nGlobalId : UDINT;              // source : station × 1000 + n° local
    nCode : UINT;
    nSubCode : UDINT;
    eSeverity : E_Severity;
    eState : E_AlarmState;
    tRaised : LINT;
    tCleared : LINT;
    nRepeat : UINT;
    bCause : BOOL;                  // cause première d'un arrêt
    stCtx : ST_AlarmContext;
END_STRUCT
END_TYPE
