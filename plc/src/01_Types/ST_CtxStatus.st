// Statut publié d'un contexte (docs/05 § 4.5)
{attribute 'pack_mode' := '1'}
TYPE ST_CtxStatus :
STRUCT
    eStatus : E_CtxStatus;
    nTable : USINT;
    nPC : INT;
    nStepId : UDINT;
    nNextStepId : UDINT;
    nObject : UINT;
    nAction : UINT;
    nRetry : USINT;
    fWaitMs : REAL;
    stNextParams : ST_ActParams;    // pas à pas : paramètres résolus de la prochaine action
END_STRUCT
END_TYPE
