// Pas à pas d'une station (docs/05 § 8)
{attribute 'pack_mode' := '1'}
TYPE ST_StepControl :
STRUCT
    bEnabled : BOOL;
    eGranularity : E_StepGranularity;
    nStepSeq : UDINT;               // la HMI l'incrémente pour autoriser un pas
END_STRUCT
END_TYPE
