// Réponse du canal manuel (docs/05 § 7)
{attribute 'pack_mode' := '1'}
TYPE ST_ManualResponse :
STRUCT
    nSeq : UDINT;
    eResult : E_CmdResult;
    eReason : E_RejectReason;
    eStart : E_StartResult;
    eActStatus : E_ActStatus;
    nErrorCode : UINT;
    nSubCode : UDINT;
    stResults : ST_ActResults;
    stTaught : ST_Position;
END_STRUCT
END_TYPE
