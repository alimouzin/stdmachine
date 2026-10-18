// Statut de la machine (docs/06 § 6.3)
{attribute 'pack_mode' := '1'}
TYPE ST_MachineStatus :
STRUCT
    eState : E_State;
    eMode : E_Mode;
    fEffOverride : REAL;
    nBeacon : UINT;
    bFault : BOOL;
    tStateSince : LINT;
    nStopCauseGlobalId : UDINT;     // cause première (docs/14 § 4)
    nStopCauseCode : UINT;
    eStopReason : E_StopReason;     // D94
    nActiveOrder : UDINT;           // n° interne, 0 = aucun
    aOrder : ARRAY[1..GVL_Const.MAX_ORDERS] OF ST_OrderState;
    bDrain : BOOL;                  // fin des pièces en cours (docs/05 § 5.7)
    nPartsInProcess : UINT;
    bLoadingLocked : BOOL;          // arrêt du chargement qualité
    eDeclared : E_DeclaredState;
    bStartBlocked : BOOL;
    tPlcClock : LINT;               // UTC
    nPlcHeartbeat : UDINT;
    sProjectHash : STRING(64);
    tDeployed : LINT;
    nCalib : USINT;
    aCalib : ARRAY[1..GVL_Const.CALIB_PER_STATION] OF ST_CalibStatus;
    stPrompt : ST_PromptState;      // calibration machine
    bHmiLost : BOOL;                // battement de la HMI perdu (alarme 917)
END_STRUCT
END_TYPE
