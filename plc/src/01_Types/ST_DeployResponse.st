// Réponse du chargeur (docs/05 § 10)
{attribute 'pack_mode' := '1'}
TYPE ST_DeployResponse :
STRUCT
    nSeq : UDINT;
    eResult : E_CmdResult;
    nErrors : USINT;
    aError : ARRAY[1..32] OF ST_DeployError;
END_STRUCT
END_TYPE
