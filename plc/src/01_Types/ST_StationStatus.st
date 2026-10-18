// Statut d'une station (docs/06 § 6.3)
{attribute 'pack_mode' := '1'}
TYPE ST_StationStatus :
STRUCT
    eState : E_State;
    eMode : E_Mode;
    fEffOverride : REAL;
    nBeacon : UINT;                 // voir F_BeaconBits
    bFault : BOOL;
    bOutOfProduction : BOOL;
    bQualityLock : BOOL;
    nOpGroup : USINT;
    nOpOperation : USINT;
    nOpMover : USINT;
    nOpSlot : USINT;
    eOpStatus : E_OpStatus;
    fOpElapsedMs : REAL;
    aCtx : ARRAY[1..GVL_Const.MAX_CTX] OF ST_CtxStatus;
    bStepEnabled : BOOL;
    eStepGranularity : E_StepGranularity;
    nCalib : USINT;
    aCalib : ARRAY[1..GVL_Const.CALIB_PER_STATION] OF ST_CalibStatus;
    stPrompt : ST_PromptState;
    nCycleCount : UDINT;
    fLastCycleMs : REAL;
END_STRUCT
END_TYPE
