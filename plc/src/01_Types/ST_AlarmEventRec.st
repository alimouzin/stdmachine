// Événement d'alarme du tampon circulaire (docs/14 § 5)
{attribute 'pack_mode' := '1'}
TYPE ST_AlarmEventRec :
STRUCT
    nSeq : UDINT;
    eEvent : E_AlarmEvent;
    tTime : LINT;
    stAlarm : ST_AlarmEntry;
    sUser : STRING(24);
END_STRUCT
END_TYPE
