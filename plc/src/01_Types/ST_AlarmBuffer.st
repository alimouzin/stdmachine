// Alarmes actives et tampon d'événements (docs/06 § 6.3)
{attribute 'pack_mode' := '1'}
TYPE ST_AlarmBuffer :
STRUCT
    nActive : UINT;
    aActive : ARRAY[1..GVL_Const.ALARM_ACTIVE] OF ST_AlarmEntry;
    nWriteSeq : UDINT;              // n° du dernier événement écrit
    aEvent : ARRAY[0..GVL_Const.ALARM_EVENTS - 1] OF ST_AlarmEventRec;
END_STRUCT
END_TYPE
