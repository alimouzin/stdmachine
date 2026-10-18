// Contexte relevé à l'apparition d'une alarme (docs/14 § 5)
{attribute 'pack_mode' := '1'}
TYPE ST_AlarmContext :
STRUCT
    nTable : USINT;
    nStepId : UDINT;
    nMover : USINT;
    nSlot : USINT;
    sPartId : STRING(40);
    nAction : UINT;
END_STRUCT
END_TYPE
