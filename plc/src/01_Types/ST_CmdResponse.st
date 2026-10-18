// Réponse commune des handshakes (docs/06 § 6.1)
{attribute 'pack_mode' := '1'}
TYPE ST_CmdResponse :
STRUCT
    nSeq : UDINT;
    eResult : E_CmdResult;
    eReason : E_RejectReason;
    eState : E_State;               // état de la cible au moment de la réponse
    nDetail : UDINT;                // précision (n° d'élément, version demandée…)
END_STRUCT
END_TYPE
