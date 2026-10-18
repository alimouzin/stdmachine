// Contexte d'exécution d'une station (docs/05 § 3.1)
TYPE ST_Context :
STRUCT
    eStatus : E_CtxStatus;
    nTable : USINT;                 // table en cours
    nPC : INT;                      // index d'instruction absolu dans aInstr
    aRetTable : ARRAY[1..GVL_Const.MAX_CALL_DEPTH] OF USINT;
    aRetPC : ARRAY[1..GVL_Const.MAX_CALL_DEPTH] OF INT;
    nDepth : USINT;
    nParent : USINT;                // contexte qui a fait le Fork, 0 sinon
    nForkPC : INT;                  // Fork de rattachement
    nChildren : USINT;              // branches filles encore actives
    bBranchFailed : BOOL;           // une branche a terminé l'opération (pièce mauvaise)
    nRetry : USINT;
    fWaitMs : LREAL;
    nStepId : UDINT;                // carte en cours
    nStepDepth : USINT;
    nErrorCode : UINT;
    bStarted : BOOL;                // l'instruction courante a déjà été lancée (Action, Prompt…)
    bStepReleased : BOOL;           // pas à pas : point d'arrêt déjà franchi pour cette instruction
END_STRUCT
END_TYPE
