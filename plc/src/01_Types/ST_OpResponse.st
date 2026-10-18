// Réponse d'une station (docs/05 § 4.1)
TYPE ST_OpResponse :
STRUCT
    nReqId : UDINT;
    eStatus : E_OpStatus;
    nErrorCode : UINT;
    nOutMask : UINT;                // bit k-1 = sortie k définie
    aOut : ARRAY[1..GVL_Const.MAX_OUTPUTS] OF LREAL;
    stOutPos : ST_Position;
END_STRUCT
END_TYPE
