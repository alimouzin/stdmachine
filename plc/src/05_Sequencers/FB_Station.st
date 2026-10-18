// Station : machine d'états PackML, interpréteur à 8 contextes, objets, demandes d'opération, préparation,
// pas à pas, calibrations, canal manuel (docs/05 § 2 à 4, § 7, § 8, § 10.1).
// Contexte 1 : opération, initialisation, arrêt, vidage, calibration ; contexte 2 : cycle de fond ;
// contextes 3 à 8 : branches des blocs En parallèle.
FUNCTION_BLOCK FB_Station EXTENDS FB_NodeBase IMPLEMENTS I_InterlockProvider, I_ManualHost
VAR
    nNumber : USINT;
    _refData : REFERENCE TO ST_StationData;
    _refMachine : REFERENCE TO ST_MachineData;
    _refAlarms : REFERENCE TO FB_AlarmManager;
    _refStatus : REFERENCE TO ST_StationStatus;
    _refStep : REFERENCE TO ST_StepControl;
    aObj : ARRAY[1..GVL_Const.MAX_OBJ_PER_STATION] OF I_Object;   // registre (docs/04 § 9)
    _nObj : UINT;
    fbPack : FB_PackML;
    fbManual : FB_ManualChannel;
    _eMode : E_Mode := E_Mode.Production;
    _ePendingCmd : E_Cmd;
    _bFault : BOOL;                 // défaut de station à appliquer (Aborted)
    _bMachineFault : BOOL;          // une alarme de process de gravité « défaut machine » a été levée
    _bOutOfProduction : BOOL;
    _bQualityLock : BOOL;
    _aCtx : ARRAY[1..GVL_Const.MAX_CTX] OF ST_Context;
    _aLoopMs : ARRAY[1..GVL_Const.MAX_CTX] OF LREAL;
    // Opération en cours
    _stReq : ST_OpRequest;
    _pMover : POINTER TO ST_MoverCtx;
    _stResp : ST_OpResponse;
    _bOpActive : BOOL;
    _bOpBad : BOOL;
    _bOpBadJump : BOOL;
    _fOpMs : LREAL;
    _bLaunched : BOOL;              // lancée par la HMI en Maintenance (docs/05 § 8)
    _bCalib : BOOL;                 // la table en cours est une calibration (docs/05 § 10.1)
    _nCalibIndex : USINT;
    _sLaunchUser : STRING(24);
    _bRemoved : BOOL;               // l'opération a marqué la pièce « retirée »
    _bHoldRequested : BOOL;
    _nStopPhase : USINT;            // 0 à lancer, 1 en cours, 2 terminée
    _nSeenStepSeq : UDINT;
    // Demande à l'opérateur (calibration)
    _stPrompt : ST_PromptState;
    _nPromptSeq : UDINT;
    _bPromptAnswered : BOOL;
    _bPromptConfirm : BOOL;
    _fPromptValue : LREAL;
    // Copie de travail d'une calibration : validée seulement à la fin réussie
    _aWorkVar : ARRAY[1..GVL_Const.MAX_VARS] OF LREAL;
    _aWorkVarPos : ARRAY[1..GVL_Const.MAX_VARS] OF ST_Position;
    _aWorkVarSet : ARRAY[1..GVL_Const.MAX_VARS] OF BOOL;
    _aWorkPos : ARRAY[1..GVL_Const.MAX_NAMED_POS] OF ST_Position;
    _aWorkPosSet : ARRAY[1..GVL_Const.MAX_NAMED_POS] OF BOOL;
    _nCycleCount : UDINT;
    _fLastCycleMs : LREAL;
    _stEmptyParams : ST_ActParams;
END_VAR
VAR PERSISTENT
    aVar : ARRAY[1..GVL_Const.MAX_VARS] OF LREAL;               // variables de station (booléen : 0 ou 1)
    aVarPos : ARRAY[1..GVL_Const.MAX_VARS] OF ST_Position;
    aVarId : ARRAY[1..GVL_Const.MAX_VARS] OF UDINT;             // identifiant de la variable à ce rang (remappage)
    aCalib : ARRAY[1..GVL_Const.CALIB_PER_STATION] OF ST_CalibStatus;
END_VAR

// ================================================================== mise en place

METHOD Init
VAR_INPUT
    nStation : USINT;
    refData : REFERENCE TO ST_StationData;
    refMachine : REFERENCE TO ST_MachineData;
    refAlarms : REFERENCE TO FB_AlarmManager;
    refStatus : REFERENCE TO ST_StationStatus;
    refStep : REFERENCE TO ST_StepControl;
    ipMachine : I_Node;
END_VAR
nNumber := nStation;
_refData REF= refData;
_refMachine REF= refMachine;
_refAlarms REF= refAlarms;
_refStatus REF= refStatus;
_refStep REF= refStep;
SetParent(ipMachine);
fbManual.Init(nStation := nStation, ipHost := THIS^);
END_METHOD

METHOD ClearObjects
VAR
    i : UINT;
END_VAR
FOR i := 1 TO GVL_Const.MAX_OBJ_PER_STATION DO aObj[i] := 0; END_FOR
_nObj := 0;
END_METHOD

METHOD SetObject
VAR_INPUT
    nLocalId : UINT;
    ipObj : I_Object;
END_VAR
IF nLocalId >= 1 AND nLocalId <= GVL_Const.MAX_OBJ_PER_STATION THEN
    aObj[nLocalId] := ipObj;
    _nObj := MAX(_nObj, nLocalId);
END_IF
END_METHOD

// Nouveau déploiement (docs/05 § 10, étape 4) : contextes et variables remis à l'état initial, sauf les
// variables persistantes retrouvées par leur identifiant et leur type.
METHOD OnDeploy
VAR
    i : INT;
    k : INT;
    aOldVar : ARRAY[1..GVL_Const.MAX_VARS] OF LREAL;
    aOldPos : ARRAY[1..GVL_Const.MAX_VARS] OF ST_Position;
    aOldId : ARRAY[1..GVL_Const.MAX_VARS] OF UDINT;
END_VAR
_KillContexts();
fbPack.ForceStopped();
_bOpActive := FALSE;
_bFault := FALSE;
_bMachineFault := FALSE;
_bHoldRequested := FALSE;
_stResp.eStatus := E_OpStatus.Idle;
aOldVar := aVar;
aOldPos := aVarPos;
aOldId := aVarId;
FOR i := 1 TO GVL_Const.MAX_VARS DO
    aVar[i] := _refData.aVar[i].fValue;
    aVarPos[i] := _refData.aVar[i].stPos;
    aVarId[i] := _refData.aVar[i].nId;
    IF _refData.aVar[i].bRetain AND _refData.aVar[i].nId <> 0 THEN
        FOR k := 1 TO GVL_Const.MAX_VARS DO
            IF aOldId[k] = _refData.aVar[i].nId THEN
                aVar[i] := aOldVar[k];
                aVarPos[i] := aOldPos[k];
            END_IF
        END_FOR
    END_IF
END_FOR
// États des calibrations retrouvés par identifiant
FOR i := 1 TO GVL_Const.CALIB_PER_STATION DO
    IF i <= _refData.nCalibCount AND aCalib[i].nId <> _refData.aCalib[i].nId THEN
        MEMSET(ADR(aCalib[i]), 0, SIZEOF(aCalib[i]));
        aCalib[i].nId := _refData.aCalib[i].nId;
    END_IF
    IF i <= _refData.nCalibCount THEN aCalib[i].nValidityDays := _refData.aCalib[i].nValidityDays; END_IF
END_FOR
END_METHOD

// Hôte du canal manuel (I_ManualHost)
METHOD ObjectAt : I_Object
VAR_INPUT
    nLocalId : UINT;
END_VAR
IF nLocalId >= 1 AND nLocalId <= GVL_Const.MAX_OBJ_PER_STATION THEN ObjectAt := aObj[nLocalId]; END_IF
END_METHOD

METHOD NamedPos : POINTER TO ST_NamedPos
VAR_INPUT
    nIndex : INT;
END_VAR
NamedPos := 0;
IF nIndex >= 1 AND nIndex <= GVL_Const.MAX_NAMED_POS AND __ISVALIDREF(_refData) THEN NamedPos := ADR(_refData.aPos[nIndex]); END_IF
END_METHOD

// ================================================================== commandes

METHOD Command : E_RejectReason
VAR_INPUT
    eCmd : E_Cmd;
END_VAR
IF NOT fbPack.Accepts(eCmd) THEN
    Command := E_RejectReason.WrongState;
    RETURN;
END_IF
_ePendingCmd := eCmd;
Command := E_RejectReason.None;
END_METHOD

METHOD SetMode : E_RejectReason
VAR_INPUT
    eMode : E_Mode;
END_VAR
IF fbPack.eState <> E_State.Stopped THEN
    SetMode := E_RejectReason.WrongState;
    RETURN;
END_IF
_eMode := eMode;
SetMode := E_RejectReason.None;
END_METHOD

METHOD SetOutOfProduction
VAR_INPUT
    bOut : BOOL;
END_VAR
_bOutOfProduction := bOut;
END_METHOD

METHOD SetQualityLock
VAR_INPUT
    bLock : BOOL;
END_VAR
_bQualityLock := bLock;
END_METHOD

// Changement d'OF : les objets concernés (compteurs) réagissent
METHOD OnOrderChange
VAR
    i : UINT;
    ipAware : I_OrderAware;
END_VAR
FOR i := 1 TO _nObj DO
    IF aObj[i] <> 0 THEN
        IF __QUERYINTERFACE(aObj[i], ipAware) THEN ipAware.OnOrderChange(); END_IF
    END_IF
END_FOR
END_METHOD

// Défaut venu de l'extérieur (Abort machine, essai)
METHOD RaiseFault
_bFault := TRUE;
END_METHOD

// ================================================================== demandes d'opération (docs/05 § 4.1)

METHOD Offer : BOOL
VAR_INPUT
    stReq : ST_OpRequest;
    pMover : POINTER TO ST_MoverCtx;
END_VAR
VAR
    i : INT;
    nTable : USINT;
END_VAR
Offer := FALSE;
IF fbPack.eState <> E_State.Execute OR _bHoldRequested OR _aCtx[1].eStatus <> E_CtxStatus.Free OR _bOpActive THEN RETURN; END_IF
FOR i := 1 TO _refData.nOpMapCount DO
    IF _refData.aOpMap[i].nGroup = stReq.nGroup AND _refData.aOpMap[i].nOperationNo = stReq.nOperation THEN
        nTable := _refData.aOpMap[i].nTable;
    END_IF
END_FOR
IF nTable = 0 THEN RETURN; END_IF
_StartOperation(stReq, pMover, nTable);
Offer := TRUE;
END_METHOD

// L'opération (fonction, n°) a-t-elle une préparation (instruction WaitMover) ? (docs/05 § 4.1)
METHOD PrepareWanted : BOOL
VAR_INPUT
    nGroup : USINT;
    nOperation : USINT;
END_VAR
VAR
    i : INT;
END_VAR
PrepareWanted := FALSE;
FOR i := 1 TO _refData.nOpMapCount DO
    IF _refData.aOpMap[i].nGroup = nGroup AND _refData.aOpMap[i].nOperationNo = nOperation THEN PrepareWanted := _refData.aOpMap[i].bPrepare; END_IF
END_FOR
END_METHOD

// Le mover est arrivé en position de travail (préparation, docs/05 § 4.1)
METHOD MoverArrived
VAR_INPUT
    nReqId : UDINT;
END_VAR
IF _bOpActive AND _stReq.nReqId = nReqId THEN _stReq.bMoverAtStation := TRUE; END_IF
END_METHOD

// Le séquenceur général a lu la réponse
METHOD AckResponse
IF _stResp.eStatus = E_OpStatus.Done OR _stResp.eStatus = E_OpStatus.Failed OR _stResp.eStatus = E_OpStatus.Aborted THEN
    _stResp.eStatus := E_OpStatus.Idle;
END_IF
END_METHOD

// Maintenance : lancer une table (opération, initialisation, vidage, calibration), docs/05 § 8
METHOD Launch : E_RejectReason
VAR_INPUT
    nTable : USINT;
    nMover : USINT;                 // 0 = sans mover
    pMover : POINTER TO ST_MoverCtx;
    nSlot : USINT;
    aParam : ARRAY[1..GVL_Const.MAX_PARAMS] OF LREAL;
    sUser : STRING(24);
END_VAR
VAR
    stReq : ST_OpRequest;
    i : INT;
END_VAR
IF _eMode <> E_Mode.Maintenance THEN Launch := E_RejectReason.WrongMode; RETURN; END_IF
IF fbPack.eState <> E_State.Idle AND fbPack.eState <> E_State.Execute THEN Launch := E_RejectReason.WrongState; RETURN; END_IF
IF _aCtx[1].eStatus <> E_CtxStatus.Free OR _bOpActive THEN Launch := E_RejectReason.Busy; RETURN; END_IF
IF nTable < 1 OR nTable > GVL_Const.MAX_TABLES OR_ELSE NOT _refData.aTable[nTable].bUsed THEN Launch := E_RejectReason.NotConfigured; RETURN; END_IF
stReq.nReqId := 0;
stReq.nMover := nMover;
stReq.nSlot := nSlot;
stReq.bMoverAtStation := TRUE;
stReq.aParam := aParam;
_bLaunched := TRUE;
_sLaunchUser := sUser;
_bCalib := _refData.aTable[nTable].eKind = E_TableKind.Calibration;
_nCalibIndex := 0;
IF _bCalib THEN
    FOR i := 1 TO _refData.nCalibCount DO
        IF _refData.aCalib[i].nTable = nTable THEN _nCalibIndex := INT_TO_USINT(i); END_IF
    END_FOR
    MEMSET(ADR(_aWorkVarSet), 0, SIZEOF(_aWorkVarSet));
    MEMSET(ADR(_aWorkPosSet), 0, SIZEOF(_aWorkPosSet));
    IF _nCalibIndex > 0 THEN aCalib[_nCalibIndex].eState := E_CalibState.Running; END_IF
END_IF
// La table tourne sur le contexte 1 sans changer l'état PackML (comme le simulateur ; écart avec docs/05 § 8 noté en D93)
_StartOperation(stReq, pMover, nTable);
Launch := E_RejectReason.None;
END_METHOD

METHOD AnswerPrompt : BOOL
VAR_INPUT
    nPromptId : UDINT;
    bConfirm : BOOL;
    fValue : LREAL;
END_VAR
AnswerPrompt := FALSE;
IF _stPrompt.nPromptId = 0 OR _stPrompt.nPromptId <> nPromptId THEN RETURN; END_IF
_bPromptAnswered := TRUE;
_bPromptConfirm := bConfirm;
_fPromptValue := fValue;
AnswerPrompt := TRUE;
END_METHOD

METHOD PRIVATE _StartOperation
VAR_INPUT
    stReq : ST_OpRequest;
    pMover : POINTER TO ST_MoverCtx;
    nTable : USINT;
END_VAR
_stReq := stReq;
_pMover := pMover;
_bOpActive := TRUE;
_bOpBad := FALSE;
_bOpBadJump := FALSE;
_bRemoved := FALSE;
_fOpMs := 0;
_stResp.nReqId := stReq.nReqId;
_stResp.nErrorCode := 0;
_stResp.nOutMask := 0;
MEMSET(ADR(_stResp.aOut), 0, SIZEOF(_stResp.aOut));
_stResp.eStatus := SEL(stReq.bMoverAtStation, E_OpStatus.Preparing, E_OpStatus.Running);
_StartContext(1, nTable);
END_METHOD

// ================================================================== cycle

METHOD Cycle
VAR
    eCmd : E_Cmd;
    eBefore : E_State;
    i : INT;
    fCycleMs : LREAL;
END_VAR
fCycleMs := F_CycleMs();
UpdateOverride();
eCmd := _ePendingCmd;
_ePendingCmd := E_Cmd.None;

_CheckSafety();
_AdvanceStopTable();
eBefore := fbPack.eState;
fbPack(eCmd := eCmd, bStateComplete := _StateComplete(), bFault := _bFault);
IF fbPack.bEntry THEN _OnEntry(eBefore); END_IF
IF fbPack.eState = E_State.Aborted THEN _bFault := FALSE; END_IF

// Pas à pas : un nouveau pas, ou le pas à pas désactivé, relâche les contextes en attente (docs/05 § 8)
IF __ISVALIDREF(_refStep) THEN
    IF _refStep.nStepSeq <> _nSeenStepSeq OR NOT _refStep.bEnabled THEN
        _nSeenStepSeq := _refStep.nStepSeq;
        FOR i := 1 TO GVL_Const.MAX_CTX DO
            IF _aCtx[i].eStatus = E_CtxStatus.WaitStep THEN
                _aCtx[i].eStatus := E_CtxStatus.Running;
                _aCtx[i].bStepReleased := TRUE;
            END_IF
        END_FOR
    END_IF
END_IF

_RunInterpreter(fCycleMs);
_FinishOperation(fCycleMs);
_UpdateObjects();
fbManual.Cycle();
_Publish();
END_METHOD

METHOD PRIVATE _CheckSafety
VAR
    i : UINT;
    ipSafety : I_SafetyCircuits;
END_VAR
FOR i := 1 TO _nObj DO
    IF aObj[i] <> 0 THEN
        IF __QUERYINTERFACE(aObj[i], ipSafety) THEN
            IF NOT ipSafety.AllOk AND fbPack.eState <> E_State.Aborted THEN _bFault := TRUE; END_IF
        END_IF
    END_IF
END_FOR
END_METHOD

METHOD PRIVATE _StateComplete : BOOL
VAR
    i : UINT;
    bObjOk : BOOL := TRUE;
END_VAR
CASE fbPack.eState OF
    E_State.Resetting:
        _StateComplete := _aCtx[1].eStatus = E_CtxStatus.Free OR _aCtx[1].eStatus = E_CtxStatus.Done;
    E_State.Stopping:
        _StateComplete := _nStopPhase = 2 AND (_aCtx[2].eStatus = E_CtxStatus.Free OR _aCtx[2].eStatus = E_CtxStatus.Holding);
    E_State.Clearing:
        FOR i := 1 TO _nObj DO
            IF aObj[i] <> 0 THEN
                IF aObj[i].NodeState = E_ObjState.Error THEN bObjOk := FALSE; END_IF
            END_IF
        END_FOR
        _StateComplete := (_aCtx[1].eStatus = E_CtxStatus.Free OR _aCtx[1].eStatus = E_CtxStatus.Done) AND bObjOk;
ELSE
    _StateComplete := FALSE;
END_CASE
END_METHOD

METHOD PRIVATE _OnEntry
VAR_INPUT
    eFrom : E_State;
END_VAR
VAR
    i : UINT;
END_VAR
CASE fbPack.eState OF
    E_State.Stopped:
        _KillContexts();
        _bHoldRequested := FALSE;
    E_State.Resetting:
        FOR i := 1 TO _nObj DO IF aObj[i] <> 0 THEN aObj[i].Ack(); END_IF END_FOR
        _StartTable(E_TableKind.Init, 1);
    E_State.Execute:
        _bHoldRequested := FALSE;
        FOR i := 1 TO GVL_Const.MAX_CTX DO
            IF _aCtx[i].eStatus = E_CtxStatus.Holding THEN _aCtx[i].eStatus := E_CtxStatus.Running; END_IF
        END_FOR
        // Le cycle de fond démarre en venant d'Idle, sauf en pas à pas (docs/05 § 8)
        IF eFrom = E_State.Idle AND _aCtx[2].eStatus = E_CtxStatus.Free AND NOT (__ISVALIDREF(_refStep) AND_THEN _refStep.bEnabled) THEN
            _StartTable(E_TableKind.Background, 2);
        END_IF
    E_State.Held:
        _bHoldRequested := TRUE;
    E_State.Stopping:
        _bHoldRequested := TRUE;
        _nStopPhase := 0;
    E_State.Idle:
        MEMSET(ADR(_aCtx[1]), 0, SIZEOF(_aCtx[1]));
    E_State.Aborted:
        // Abort : enfants d'abord (les n° locaux sont attribués parents d'abord, docs/04 § 8)
        FOR i := _nObj TO 1 BY -1 DO IF aObj[i] <> 0 THEN aObj[i].Abort(); END_IF END_FOR
        _KillContexts();
        _bPromptAnswered := FALSE;
        _stPrompt.nPromptId := 0;
        IF _bOpActive THEN
            _stResp.eStatus := E_OpStatus.Aborted;
            _EndOperation(FALSE);
        END_IF
    E_State.Clearing:
        IF __ISVALIDREF(_refAlarms) THEN _refAlarms.AckFaults(nNumber, FALSE, ''); END_IF
        FOR i := 1 TO _nObj DO IF aObj[i] <> 0 THEN aObj[i].Ack(); END_IF END_FOR
        _bHoldRequested := FALSE;
        _bMachineFault := FALSE;
        _StartTable(E_TableKind.Clear, 1);
END_CASE
END_METHOD

// Stopping : l'opération en cours se termine ; une préparation qui attend son mover est annulée (code 907) ;
// puis la table Arrêt tourne sur le contexte 1 (docs/05 § 4.2 et 4.2 bis)
METHOD PRIVATE _AdvanceStopTable
IF fbPack.eState <> E_State.Stopping THEN RETURN; END_IF
IF _bOpActive AND NOT _stReq.bMoverAtStation THEN
    _KillContext(1);
    IF __ISVALIDREF(_refAlarms) THEN _refAlarms.RaiseEvent(_GlobalId(), 907, _stReq.nMover, E_Severity.Info); END_IF
    _stResp.eStatus := E_OpStatus.Aborted;
    _stResp.nErrorCode := 907;
    _EndOperation(FALSE);
END_IF
IF _nStopPhase = 0 AND _aCtx[1].eStatus = E_CtxStatus.Free AND NOT _bOpActive THEN
    _nStopPhase := SEL(_StartTable(E_TableKind.Stop, 1), 2, 1);
ELSIF _nStopPhase = 1 AND (_aCtx[1].eStatus = E_CtxStatus.Done OR _aCtx[1].eStatus = E_CtxStatus.Free) THEN
    MEMSET(ADR(_aCtx[1]), 0, SIZEOF(_aCtx[1]));
    _nStopPhase := 2;
END_IF
END_METHOD

METHOD PRIVATE _StartTable : BOOL
VAR_INPUT
    eKind : E_TableKind;
    nCtx : USINT;
END_VAR
VAR
    t : USINT;
END_VAR
_StartTable := FALSE;
FOR t := 1 TO GVL_Const.MAX_TABLES DO
    IF _refData.aTable[t].bUsed AND _refData.aTable[t].eKind = eKind THEN
        _StartContext(nCtx, t);
        _StartTable := TRUE;
        RETURN;
    END_IF
END_FOR
END_METHOD

METHOD PRIVATE _StartContext
VAR_INPUT
    nCtx : USINT;
    nTable : USINT;
END_VAR
MEMSET(ADR(_aCtx[nCtx]), 0, SIZEOF(_aCtx[nCtx]));
_aCtx[nCtx].eStatus := E_CtxStatus.Running;
_aCtx[nCtx].nTable := nTable;
_aCtx[nCtx].nPC := _refData.aTable[nTable].nStart;
_aLoopMs[nCtx] := 0;
END_METHOD

METHOD PRIVATE _KillContext
VAR_INPUT
    nCtx : USINT;
END_VAR
MEMSET(ADR(_aCtx[nCtx]), 0, SIZEOF(_aCtx[nCtx]));
END_METHOD

METHOD PRIVATE _KillContexts
VAR
    i : USINT;
END_VAR
FOR i := 1 TO GVL_Const.MAX_CTX DO _KillContext(i); END_FOR
END_METHOD

METHOD PRIVATE _FinishOperation
VAR_INPUT
    fCycleMs : LREAL;
END_VAR
IF NOT _bOpActive THEN RETURN; END_IF
_fOpMs := _fOpMs + fCycleMs;
IF _stResp.eStatus = E_OpStatus.Preparing AND _stReq.bMoverAtStation THEN _stResp.eStatus := E_OpStatus.Running; END_IF
IF _aCtx[1].eStatus = E_CtxStatus.Done THEN
    _stResp.eStatus := SEL(_bOpBad, E_OpStatus.Done, E_OpStatus.Failed);
    _KillContext(1);
    _nCycleCount := _nCycleCount + 1;
    _fLastCycleMs := _fOpMs;
    _EndOperation(NOT _bOpBad);
ELSIF _aCtx[1].eStatus = E_CtxStatus.Failed THEN
    _stResp.eStatus := E_OpStatus.Failed;
    _stResp.nErrorCode := _aCtx[1].nErrorCode;
    _KillContext(1);
    _EndOperation(FALSE);
END_IF
END_METHOD

METHOD PRIVATE _EndOperation
VAR_INPUT
    bOk : BOOL;
END_VAR
IF _bCalib THEN _CommitCalibration(bOk); END_IF
_bOpActive := FALSE;
_bLaunched := FALSE;
_bCalib := FALSE;
_stPrompt.nPromptId := 0;
END_METHOD

// Calibration : copie de travail validée seulement à la fin réussie (docs/05 § 10.1)
METHOD PRIVATE _CommitCalibration
VAR_INPUT
    bOk : BOOL;
END_VAR
VAR
    i : INT;
END_VAR
IF bOk THEN
    FOR i := 1 TO GVL_Const.MAX_VARS DO
        IF _aWorkVarSet[i] THEN
            aVar[i] := _aWorkVar[i];
            aVarPos[i] := _aWorkVarPos[i];
        END_IF
    END_FOR
    FOR i := 1 TO GVL_Const.MAX_NAMED_POS DO
        IF _aWorkPosSet[i] THEN
            _refData.aPos[i].stPos := _aWorkPos[i];
            _refData.aPos[i].bTaught := TRUE;
        END_IF
    END_FOR
END_IF
IF _nCalibIndex > 0 THEN
    aCalib[_nCalibIndex].eState := SEL(bOk, E_CalibState.Failed, E_CalibState.Valid);
    IF bOk THEN
        aCalib[_nCalibIndex].tLast := F_NowUtc();
        aCalib[_nCalibIndex].sUser := _sLaunchUser;
    END_IF
END_IF
MEMSET(ADR(_aWorkVarSet), 0, SIZEOF(_aWorkVarSet));
MEMSET(ADR(_aWorkPosSet), 0, SIZEOF(_aWorkPosSet));
END_METHOD

// Objets appelés dans l'ordre des n° locaux (parents d'abord) ; état agrégé des modules (docs/04 § 8)
METHOD PRIVATE _UpdateObjects
VAR
    i : UINT;
    nParent : UINT;
    aAgg : ARRAY[1..GVL_Const.MAX_OBJ_PER_STATION] OF E_ObjState;
    ipGroup : I_Group;
    eStationAgg : E_ObjState := E_ObjState.Ready;
END_VAR
FOR i := 1 TO _nObj DO
    IF aObj[i] <> 0 THEN
        aObj[i].Cycle();
        GVL_Hmi.aObjStatus[nNumber, i] := aObj[i].Status;      // publication pour la HMI (docs/06 § 6.3)
    END_IF
    aAgg[i] := E_ObjState.Ready;
END_FOR
FOR i := _nObj TO 1 BY -1 DO
    IF aObj[i] <> 0 THEN
        nParent := aObj[i].Info.nParentId;
        IF nParent >= 1 AND nParent <= GVL_Const.MAX_OBJ_PER_STATION THEN
            aAgg[nParent] := F_AggregateState(aAgg[nParent], F_AggregateState(aAgg[i], aObj[i].Status.eState));
        ELSE
            eStationAgg := F_AggregateState(eStationAgg, F_AggregateState(aAgg[i], aObj[i].Status.eState));
        END_IF
    END_IF
END_FOR
FOR i := 1 TO _nObj DO
    IF aObj[i] <> 0 THEN
        IF __QUERYINTERFACE(aObj[i], ipGroup) THEN ipGroup.SetAggregate(aAgg[i]); END_IF
    END_IF
END_FOR
SetState(eStationAgg);
END_METHOD

// ================================================================== interpréteur (docs/05 § 3)

METHOD PRIVATE _RunInterpreter
VAR_INPUT
    fCycleMs : LREAL;
END_VAR
VAR
    i : USINT;
    nBudget : INT;
    bBlocked : BOOL;
END_VAR
FOR i := 1 TO GVL_Const.MAX_CTX DO
    IF _aCtx[i].eStatus = E_CtxStatus.Running OR _aCtx[i].eStatus = E_CtxStatus.WaitAction OR _aCtx[i].eStatus = E_CtxStatus.WaitCond
       OR _aCtx[i].eStatus = E_CtxStatus.WaitTime OR _aCtx[i].eStatus = E_CtxStatus.WaitJoin THEN
        nBudget := GVL_Const.MAX_INSTR_PER_CYCLE;
        REPEAT
            bBlocked := _ExecuteOne(i, fCycleMs);
            nBudget := nBudget - 1;
        UNTIL bBlocked OR nBudget = 0 OR _aCtx[i].eStatus = E_CtxStatus.Done OR _aCtx[i].eStatus = E_CtxStatus.Failed
              OR _aCtx[i].eStatus = E_CtxStatus.Free
        END_REPEAT
        // Boucle sans attente : 1 s de dépassements répétés lève l'alarme 901 et arrête la station
        IF NOT bBlocked AND nBudget = 0 AND _aCtx[i].eStatus = E_CtxStatus.Running THEN
            _aLoopMs[i] := _aLoopMs[i] + fCycleMs;
            IF _aLoopMs[i] >= GVL_Const.LOOP_GUARD_MS THEN _FailContext(i, 901, _aCtx[i].nTable); END_IF
        ELSE
            _aLoopMs[i] := 0;
        END_IF
    END_IF
END_FOR
END_METHOD

// Exécute ou fait avancer l'instruction au PC ; TRUE si le contexte est bloqué jusqu'au cycle suivant
METHOD PRIVATE _ExecuteOne : BOOL
VAR_INPUT
    nCtx : USINT;
    fCycleMs : LREAL;
END_VAR
VAR
    c : REFERENCE TO ST_Context;
    I : REFERENCE TO ST_Instr;
    nStart : INT;
END_VAR
c REF= _aCtx[nCtx];
IF c.nPC < 1 OR c.nPC > _refData.nInstrCount THEN
    _FailContext(nCtx, 905, INT_TO_UDINT(c.nPC));
    _ExecuteOne := TRUE;
    RETURN;
END_IF
I REF= _refData.aInstr[c.nPC];
_ExecuteOne := FALSE;
CASE I.eOp OF
    E_Op.Nop:
        c.nPC := c.nPC + 1;

    E_Op.StepBegin:
        // Hold : le cycle de fond s'arrête au prochain début de carte (docs/05 § 4.3)
        IF _bHoldRequested AND nCtx = 2 AND I.aArg[1] = 1 THEN c.eStatus := E_CtxStatus.Holding; _ExecuteOne := TRUE; RETURN; END_IF
        IF _StepWanted(c, I.aArg[1] = 1, E_StepGranularity.Step) THEN c.eStatus := E_CtxStatus.WaitStep; _ExecuteOne := TRUE; RETURN; END_IF
        IF I.aArg[1] = 1 THEN c.bStepReleased := FALSE; END_IF
        c.nStepId := I.nStepId;
        c.nStepDepth := c.nStepDepth + 1;
        c.nPC := c.nPC + 1;

    E_Op.StepEnd:
        IF c.nStepDepth > 0 THEN c.nStepDepth := c.nStepDepth - 1; END_IF
        c.nPC := c.nPC + 1;

    E_Op.Action:
        _ExecuteOne := _ExecAction(nCtx);

    E_Op.WaitCond:
        IF NOT c.bStarted AND _StepWanted(c, TRUE, E_StepGranularity.Action) THEN c.eStatus := E_CtxStatus.WaitStep; _ExecuteOne := TRUE; RETURN; END_IF
        c.bStarted := TRUE;
        IF _EvalCond(DINT_TO_INT(I.aArg[1])) THEN
            _NextInstr(c);
        ELSE
            c.eStatus := E_CtxStatus.WaitCond;
            c.fWaitMs := c.fWaitMs + fCycleMs;
            IF I.nTimeoutMs > 0 AND c.fWaitMs > UDINT_TO_LREAL(I.nTimeoutMs) THEN _FailContext(nCtx, 902, c.nStepId); END_IF
            _ExecuteOne := TRUE;
        END_IF

    E_Op.WaitTime:
        IF NOT c.bStarted AND _StepWanted(c, TRUE, E_StepGranularity.Action) THEN c.eStatus := E_CtxStatus.WaitStep; _ExecuteOne := TRUE; RETURN; END_IF
        c.bStarted := TRUE;
        c.fWaitMs := c.fWaitMs + fCycleMs;
        IF c.fWaitMs >= _Resolve(I.aParam[1]) THEN
            _NextInstr(c);
        ELSE
            c.eStatus := E_CtxStatus.WaitTime;
            _ExecuteOne := TRUE;
        END_IF

    E_Op.Jump:
        c.nPC := DINT_TO_INT(I.aArg[1]);

    E_Op.JumpIfFalse:
        IF _EvalCond(DINT_TO_INT(I.aArg[1])) THEN c.nPC := c.nPC + 1; ELSE c.nPC := DINT_TO_INT(I.aArg[2]); END_IF

    E_Op.Fork:
        _ExecuteOne := _Fork(nCtx);

    E_Op.Join:
        _ExecuteOne := _Join(nCtx);

    E_Op.LoopInit:
        _WriteVar(DINT_TO_INT(I.aArg[1]), _Resolve(I.aParam[1]));
        c.nPC := c.nPC + 1;

    E_Op.LoopNext:
        _WriteVar(DINT_TO_INT(I.aArg[1]), _ReadVar(DINT_TO_INT(I.aArg[1])) - 1);
        IF _ReadVar(DINT_TO_INT(I.aArg[1])) > 0 THEN c.nPC := DINT_TO_INT(I.aArg[2]); ELSE c.nPC := c.nPC + 1; END_IF

    E_Op.Call:
        IF c.nDepth >= GVL_Const.MAX_CALL_DEPTH THEN _FailContext(nCtx, 904, c.nTable); _ExecuteOne := TRUE; RETURN; END_IF
        c.nDepth := c.nDepth + 1;
        c.aRetTable[c.nDepth] := c.nTable;
        c.aRetPC[c.nDepth] := c.nPC + 1;
        c.nTable := DINT_TO_USINT(I.aArg[1]);
        c.nPC := _refData.aTable[c.nTable].nStart;

    E_Op.Return:
        IF c.nDepth = 0 THEN
            _EndContext(nCtx);
            _ExecuteOne := TRUE;
        ELSE
            c.nTable := c.aRetTable[c.nDepth];
            c.nPC := c.aRetPC[c.nDepth];
            c.nDepth := c.nDepth - 1;
        END_IF

    E_Op.SetVar:
        _SetVar(nCtx);
        c.nPC := c.nPC + 1;

    E_Op.SetOut:
        _SetOut(nCtx);
        c.nPC := c.nPC + 1;

    E_Op.ResetMoverVars:
        _ResetMoverVars(DINT_TO_INT(I.aArg[1]));
        c.nPC := c.nPC + 1;

    E_Op.Alarm:
        _ExecuteOne := _Alarm(nCtx);

    E_Op.WaitMover:
        // Préparation : attendre que le mover soit en position de travail (docs/05 § 4.2 bis)
        IF _stReq.bMoverAtStation THEN
            c.eStatus := E_CtxStatus.Running;
            c.nPC := c.nPC + 1;
        ELSIF _bHoldRequested THEN
            c.eStatus := E_CtxStatus.Holding;
            _ExecuteOne := TRUE;
        ELSE
            c.eStatus := E_CtxStatus.WaitCond;
            _ExecuteOne := TRUE;
        END_IF

    E_Op.Prompt:
        _ExecuteOne := _Prompt(nCtx);

    E_Op.SavePos:
        _SavePos(nCtx);
        c.nPC := c.nPC + 1;

    E_Op.End:
        IF I.aArg[1] = 1 THEN
            nStart := _refData.aTable[c.nTable].nStart;
            c.nPC := nStart;
            IF nCtx = 2 THEN
                IF _bHoldRequested THEN c.eStatus := E_CtxStatus.Holding; END_IF
                _ExecuteOne := TRUE;            // un tour de cycle de fond par cycle PLC au plus
            END_IF
        ELSE
            _EndContext(nCtx);
            _ExecuteOne := TRUE;
        END_IF
ELSE
    c.nPC := c.nPC + 1;                         // Route, CalibStation : instructions du séquenceur général
END_CASE
END_METHOD

METHOD PRIVATE _NextInstr
VAR_IN_OUT
    c : ST_Context;
END_VAR
c.fWaitMs := 0;
c.eStatus := E_CtxStatus.Running;
c.bStepReleased := FALSE;
c.bStarted := FALSE;
c.nPC := c.nPC + 1;
END_METHOD

// Faut-il s'arrêter ici en pas à pas ?
METHOD PRIVATE _StepWanted : BOOL
VAR_IN_OUT
    c : ST_Context;
END_VAR
VAR_INPUT
    bPoint : BOOL;                  // l'instruction est un point d'arrêt possible
    eLevel : E_StepGranularity;
END_VAR
_StepWanted := bPoint AND __ISVALIDREF(_refStep) AND_THEN (_refStep.bEnabled AND NOT c.bStepReleased AND _refStep.eGranularity = eLevel);
END_METHOD

METHOD PRIVATE _EndContext
VAR_INPUT
    nCtx : USINT;
END_VAR
IF _aCtx[nCtx].nParent <> 0 THEN
    _KillContext(nCtx);
ELSE
    _aCtx[nCtx].eStatus := E_CtxStatus.Done;
END_IF
END_METHOD

METHOD PRIVATE _FailContext
VAR_INPUT
    nCtx : USINT;
    nCode : UINT;
    nSubCode : UDINT;
END_VAR
_aCtx[nCtx].nErrorCode := nCode;
_aCtx[nCtx].eStatus := E_CtxStatus.Failed;
IF __ISVALIDREF(_refAlarms) THEN _refAlarms.RaiseEvent(_GlobalId(), nCode, nSubCode, E_Severity.StationFault); END_IF
_bFault := TRUE;
END_METHOD

METHOD PRIVATE _GlobalId : UDINT
_GlobalId := USINT_TO_UDINT(nNumber) * 1000;
END_METHOD

// ---------------------------------------------------------------- Action

METHOD PRIVATE _ExecAction : BOOL
VAR_INPUT
    nCtx : USINT;
END_VAR
VAR
    c : REFERENCE TO ST_Context;
    I : REFERENCE TO ST_Instr;
    stParams : ST_ActParams;
    eStart : E_StartResult;
    eAct : E_ActStatus;
END_VAR
c REF= _aCtx[nCtx];
I REF= _refData.aInstr[c.nPC];
IF I.nObject < 1 OR I.nObject > GVL_Const.MAX_OBJ_PER_STATION OR_ELSE aObj[I.nObject] = 0 THEN
    _FailContext(nCtx, GVL_Catalog.Err_BadParam, I.nObject);
    _ExecAction := TRUE;
    RETURN;
END_IF
IF c.eStatus <> E_CtxStatus.WaitAction THEN
    IF NOT c.bStarted AND _StepWanted(c, TRUE, E_StepGranularity.Action) THEN c.eStatus := E_CtxStatus.WaitStep; _ExecAction := TRUE; RETURN; END_IF
    c.bStepReleased := FALSE;
    _ResolveParams(I, stParams);
    stParams.nTimeoutMs := I.nTimeoutMs;
    _SetAlarmContext(nCtx, I.nAction);
    eStart := aObj[I.nObject].Start(I.nAction, stParams, FALSE);
    IF eStart <> E_StartResult.Accepted THEN
        _ExecAction := _HandleFailure(nCtx, F_StartRefusalCode(eStart));
        RETURN;
    END_IF
    c.eStatus := E_CtxStatus.WaitAction;
    c.bStarted := TRUE;
END_IF
eAct := aObj[I.nObject].ActStatus;
CASE eAct OF
    E_ActStatus.Busy:
        _ExecAction := TRUE;
    E_ActStatus.Done:
        _StoreResults(nCtx, aObj[I.nObject]);
        c.nRetry := 0;
        _NextInstr(c);
        _ExecAction := FALSE;
    E_ActStatus.Error:
        _ExecAction := _HandleFailure(nCtx, aObj[I.nObject].Status.nErrorCode);
ELSE
    c.eStatus := E_CtxStatus.Running;       // objet arrêté par Abort : le contexte sera tué ailleurs
    _ExecAction := TRUE;
END_CASE
END_METHOD

// Politique d'échec de l'instruction (docs/05 § 3.6)
METHOD PRIVATE _HandleFailure : BOOL
VAR_INPUT
    nCtx : USINT;
    nCode : UINT;
END_VAR
VAR
    c : REFERENCE TO ST_Context;
    I : REFERENCE TO ST_Instr;
    ipObj : I_Object;
    nGid : UDINT;
END_VAR
c REF= _aCtx[nCtx];
I REF= _refData.aInstr[c.nPC];
ipObj := aObj[I.nObject];
nGid := USINT_TO_UDINT(nNumber) * 1000 + UINT_TO_UDINT(I.nObject);
c.bStarted := FALSE;
CASE I.eOnFail OF
    E_FailPolicy.Retry:
        IF c.nRetry < I.nRetries THEN
            ipObj.Ack();
            IF __ISVALIDREF(_refAlarms) THEN _refAlarms.Downgrade(nGid, nCode); END_IF
            c.nRetry := c.nRetry + 1;
            c.eStatus := E_CtxStatus.Running;   // l'instruction sera relancée au prochain passage
            _HandleFailure := TRUE;
            RETURN;
        END_IF
    E_FailPolicy.PartBad:
        ipObj.Ack();
        IF __ISVALIDREF(_refAlarms) THEN _refAlarms.Downgrade(nGid, nCode); END_IF
        _SetVerdict(E_Verdict.Bad);
        _JumpToEnd(nCtx);
        _HandleFailure := FALSE;
        RETURN;
    E_FailPolicy.Goto:
        ipObj.Ack();
        IF __ISVALIDREF(_refAlarms) THEN _refAlarms.Downgrade(nGid, nCode); END_IF
        c.eStatus := E_CtxStatus.Running;
        c.nRetry := 0;
        c.nPC := I.nFailTarget;
        _HandleFailure := FALSE;
        RETURN;
    E_FailPolicy.Ignore:
        ipObj.Ack();
        IF __ISVALIDREF(_refAlarms) THEN _refAlarms.Downgrade(nGid, nCode); _refAlarms.RaiseEvent(nGid, nCode, 0, E_Severity.Warning); END_IF
        c.nRetry := 0;
        _NextInstr(c);
        _HandleFailure := FALSE;
        RETURN;
END_CASE
// Arrêter la station (défaut) : l'alarme de l'objet reste un défaut de station
c.nErrorCode := nCode;
c.eStatus := E_CtxStatus.Failed;
_bFault := TRUE;
_HandleFailure := TRUE;
END_METHOD

// Pièce mauvaise : fin de l'opération ; dans une branche, saut au Join et marque (docs/05 § 3.6)
METHOD PRIVATE _JumpToEnd
VAR_INPUT
    nCtx : USINT;
END_VAR
VAR
    c : REFERENCE TO ST_Context;
    nTable : USINT;
END_VAR
c REF= _aCtx[nCtx];
_bOpBad := TRUE;
c.nRetry := 0;
c.eStatus := E_CtxStatus.Running;
IF c.nParent <> 0 THEN
    _bOpBadJump := TRUE;
    c.nPC := DINT_TO_INT(_refData.aInstr[c.nForkPC].aArg[1]);      // Join du bloc
    RETURN;
END_IF
nTable := SEL(c.nDepth > 0, c.nTable, c.aRetTable[1]);
c.nDepth := 0;
c.nTable := nTable;
c.nPC := _refData.aTable[nTable].nStart + _refData.aTable[nTable].nLength - 1;   // instruction End de la table
END_METHOD

// ---------------------------------------------------------------- Fork / Join

METHOD PRIVATE _Fork : BOOL
VAR_INPUT
    nCtx : USINT;
END_VAR
VAR
    c : REFERENCE TO ST_Context;
    I : REFERENCE TO ST_Instr;
    aFree : ARRAY[1..4] OF USINT;
    nBranches : USINT;
    k : USINT;
    j : USINT;
    n : USINT;
    bTaken : BOOL;
END_VAR
c REF= _aCtx[nCtx];
I REF= _refData.aInstr[c.nPC];
nBranches := 0;
FOR k := 2 TO 5 DO
    IF I.aArg[k] <> 0 THEN nBranches := nBranches + 1; END_IF
END_FOR
// un contexte libre parmi 3..8 pour chaque branche après la première
n := 0;
FOR k := 2 TO nBranches DO
    FOR j := 3 TO GVL_Const.MAX_CTX DO
        bTaken := (n >= 1 AND aFree[1] = j) OR (n >= 2 AND aFree[2] = j) OR (n >= 3 AND aFree[3] = j);
        IF _aCtx[j].eStatus = E_CtxStatus.Free AND NOT bTaken AND n < k - 1 THEN
            n := n + 1;
            aFree[n] := j;
        END_IF
    END_FOR
END_FOR
IF n < nBranches - 1 THEN
    _FailContext(nCtx, 903, c.nStepId);
    _Fork := TRUE;
    RETURN;
END_IF
FOR k := 2 TO nBranches DO
    j := aFree[k - 1];
    MEMSET(ADR(_aCtx[j]), 0, SIZEOF(_aCtx[j]));
    _aCtx[j].eStatus := E_CtxStatus.Running;
    _aCtx[j].nTable := c.nTable;
    _aCtx[j].nPC := DINT_TO_INT(I.aArg[k + 1]);
    _aCtx[j].nParent := nCtx;
    _aCtx[j].nForkPC := c.nPC;
    _aCtx[j].nStepId := c.nStepId;
    _aCtx[j].nStepDepth := c.nStepDepth;
END_FOR
c.nChildren := nBranches - 1;
c.nPC := DINT_TO_INT(I.aArg[2]);
_Fork := FALSE;
END_METHOD

METHOD PRIVATE _Join : BOOL
VAR_INPUT
    nCtx : USINT;
END_VAR
VAR
    c : REFERENCE TO ST_Context;
    I : REFERENCE TO ST_Instr;
    nTable : USINT;
END_VAR
c REF= _aCtx[nCtx];
I REF= _refData.aInstr[c.nPC];
IF c.nParent <> 0 AND c.nForkPC = DINT_TO_INT(I.aArg[1]) THEN
    IF _aCtx[c.nParent].nChildren > 0 THEN _aCtx[c.nParent].nChildren := _aCtx[c.nParent].nChildren - 1; END_IF
    _KillContext(nCtx);
    _Join := TRUE;
    RETURN;
END_IF
IF c.nChildren = 0 THEN
    IF _bOpBadJump AND nCtx = 1 THEN
        _bOpBadJump := FALSE;
        nTable := c.nTable;
        c.nPC := _refData.aTable[nTable].nStart + _refData.aTable[nTable].nLength - 1;
    ELSE
        c.nPC := c.nPC + 1;
    END_IF
    c.eStatus := E_CtxStatus.Running;
    _Join := FALSE;
ELSE
    c.eStatus := E_CtxStatus.WaitJoin;
    _Join := TRUE;
END_IF
END_METHOD

// ---------------------------------------------------------------- SetVar, SetOut, Alarm, Prompt, SavePos

METHOD PRIVATE _SetVar
VAR_INPUT
    nCtx : USINT;
END_VAR
VAR
    I : REFERENCE TO ST_Instr;
    fValue : LREAL;
    fCurrent : LREAL;
    stPos : ST_Position;
    bPos : BOOL;
    pPart : POINTER TO ST_Part;
END_VAR
I REF= _refData.aInstr[_aCtx[nCtx].nPC];
bPos := _ResolvePos(I.aParam[1], stPos);
fValue := _Resolve(I.aParam[1]);
CASE I.aBind[1].eTarget OF
    E_BindTarget.Var:
        IF bPos THEN
            _WriteVarPos(I.aBind[1].nIndex, stPos);
        ELSE
            fCurrent := _ReadVar(I.aBind[1].nIndex);
            _WriteVar(I.aBind[1].nIndex, F_ApplyOp(fCurrent, fValue, I.aArg[1]));
        END_IF
    E_BindTarget.PartData:
        pPart := _CurrentPart();
        IF pPart <> 0 AND I.aBind[1].nIndex >= 1 AND I.aBind[1].nIndex <= GVL_Const.MAX_PART_DATA THEN
            pPart^.aData[I.aBind[1].nIndex] := F_ApplyOp(pPart^.aData[I.aBind[1].nIndex], fValue, I.aArg[1]);
        END_IF
    E_BindTarget.Verdict:
        _SetVerdict(LREAL_TO_USINT(fValue));
    E_BindTarget.PartRemoved:
        pPart := _CurrentPart();
        IF pPart <> 0 AND fValue <> 0 THEN
            pPart^.eStatus := E_PartStatus.Removed;
            _bRemoved := TRUE;
        END_IF
    E_BindTarget.MoverData:
        IF _pMover <> 0 AND I.aBind[1].nIndex >= 1 AND I.aBind[1].nIndex <= GVL_Const.MAX_MOVER_VARS THEN
            IF bPos THEN
                _pMover^.aVarPos[I.aBind[1].nIndex] := stPos;
            ELSE
                _pMover^.aVar[I.aBind[1].nIndex] := F_ApplyOp(_pMover^.aVar[I.aBind[1].nIndex], fValue, I.aArg[1]);
            END_IF
        END_IF
    E_BindTarget.Output:
        _WriteOutput(I.aBind[1].nIndex, 0, fValue, stPos, bPos);
END_CASE
END_METHOD

// Sortie d'un bloc composé (docs/02 § 5.5) : aArg[1] rang, aArg[2] variable cachée d'un sous-process (0 = opération)
METHOD PRIVATE _SetOut
VAR_INPUT
    nCtx : USINT;
END_VAR
VAR
    I : REFERENCE TO ST_Instr;
    stPos : ST_Position;
    bPos : BOOL;
END_VAR
I REF= _refData.aInstr[_aCtx[nCtx].nPC];
bPos := _ResolvePos(I.aParam[1], stPos);
_WriteOutput(DINT_TO_INT(I.aArg[1]), DINT_TO_INT(I.aArg[2]), _Resolve(I.aParam[1]), stPos, bPos);
END_METHOD

METHOD PRIVATE _WriteOutput
VAR_INPUT
    nRank : INT;
    nVar : INT;
    fValue : LREAL;
    stPos : ST_Position;
    bPos : BOOL;
END_VAR
IF nVar > 0 THEN
    IF bPos THEN _WriteVarPos(nVar, stPos); ELSE _WriteVar(nVar, fValue); END_IF
ELSIF nRank >= 1 AND nRank <= GVL_Const.MAX_OUTPUTS THEN
    IF bPos THEN _stResp.stOutPos := stPos; ELSE _stResp.aOut[nRank] := fValue; END_IF
    _stResp.nOutMask := _stResp.nOutMask OR SHL(UINT#1, nRank - 1);
END_IF
END_METHOD

METHOD PRIVATE _ResetMoverVars
VAR_INPUT
    nRank : INT;                    // 0 = toutes celles dont la règle n'est pas « jamais »
END_VAR
VAR
    i : INT;
END_VAR
IF _pMover = 0 THEN RETURN; END_IF
FOR i := 1 TO _refMachine.nMoverVar DO
    IF (nRank = 0 AND _refMachine.aMoverVar[i].eReset <> E_MoverVarReset.Never) OR nRank = i THEN
        _pMover^.aVar[i] := _refMachine.aMoverVar[i].fInit;
        _pMover^.aVarPos[i] := _refMachine.aMoverVar[i].stInitPos;
    END_IF
END_FOR
END_METHOD

METHOD PRIVATE _Alarm : BOOL
VAR_INPUT
    nCtx : USINT;
END_VAR
VAR
    I : REFERENCE TO ST_Instr;
    eSev : E_Severity;
END_VAR
I REF= _refData.aInstr[_aCtx[nCtx].nPC];
CASE I.aArg[2] OF
    1: eSev := E_Severity.Warning;
    3: eSev := E_Severity.MachineFault;
ELSE
    eSev := E_Severity.StationFault;
END_CASE
_SetAlarmContext(nCtx, 0);
IF __ISVALIDREF(_refAlarms) THEN _refAlarms.RaiseEvent(_GlobalId(), DINT_TO_UINT(I.aArg[3]), DINT_TO_UDINT(I.aArg[1]), eSev); END_IF
IF I.aArg[2] >= 2 THEN
    _aCtx[nCtx].nErrorCode := DINT_TO_UINT(I.aArg[3]);
    _aCtx[nCtx].eStatus := E_CtxStatus.Failed;
    _bFault := TRUE;
    IF I.aArg[2] = 3 THEN _bMachineFault := TRUE; END_IF
    _Alarm := TRUE;
ELSE
    _aCtx[nCtx].nPC := _aCtx[nCtx].nPC + 1;
    _Alarm := FALSE;
END_IF
END_METHOD

// Demande à l'opérateur (calibration, docs/05 § 10.1) : aArg[1] texte, aArg[2] 0 confirmation, 1 saisie
METHOD PRIVATE _Prompt : BOOL
VAR_INPUT
    nCtx : USINT;
END_VAR
VAR
    c : REFERENCE TO ST_Context;
    I : REFERENCE TO ST_Instr;
END_VAR
c REF= _aCtx[nCtx];
I REF= _refData.aInstr[c.nPC];
IF NOT c.bStarted THEN
    c.bStarted := TRUE;
    _nPromptSeq := _nPromptSeq + 1;
    _stPrompt.nPromptId := _nPromptSeq;
    _stPrompt.nText := DINT_TO_INT(I.aArg[1]);
    _stPrompt.bInput := I.aArg[2] = 1;
    _bPromptAnswered := FALSE;
END_IF
IF NOT _bPromptAnswered THEN
    c.eStatus := E_CtxStatus.WaitCond;
    _Prompt := TRUE;
    RETURN;
END_IF
_stPrompt.nPromptId := 0;
_bPromptAnswered := FALSE;
IF NOT _bPromptConfirm THEN
    _FailContext(nCtx, 908, c.nStepId);         // demande annulée
    _Prompt := TRUE;
    RETURN;
END_IF
IF _stPrompt.bInput AND I.aBind[1].eTarget = E_BindTarget.Var THEN _WriteVar(I.aBind[1].nIndex, _fPromptValue); END_IF
_NextInstr(c);
_Prompt := FALSE;
END_METHOD

// Enregistrer la position actuelle (calibration) : aArg[1] position nommée, aArg[2..5] objets qui donnent X, Y, Z, C
METHOD PRIVATE _SavePos
VAR_INPUT
    nCtx : USINT;
END_VAR
VAR
    I : REFERENCE TO ST_Instr;
    nPos : INT;
    stPos : ST_Position;
END_VAR
I REF= _refData.aInstr[_aCtx[nCtx].nPC];
nPos := DINT_TO_INT(I.aArg[1]);
IF nPos < 1 OR nPos > GVL_Const.MAX_NAMED_POS THEN RETURN; END_IF
stPos := _refData.aPos[nPos].stPos;
IF _aWorkPosSet[nPos] THEN stPos := _aWorkPos[nPos]; END_IF
stPos.fX := _AxisValue(DINT_TO_UINT(I.aArg[2]), stPos.fX);
stPos.fY := _AxisValue(DINT_TO_UINT(I.aArg[3]), stPos.fY);
stPos.fZ := _AxisValue(DINT_TO_UINT(I.aArg[4]), stPos.fZ);
stPos.fC := _AxisValue(DINT_TO_UINT(I.aArg[5]), stPos.fC);
IF _bCalib THEN
    _aWorkPos[nPos] := stPos;
    _aWorkPosSet[nPos] := TRUE;
ELSE
    _refData.aPos[nPos].stPos := stPos;
    _refData.aPos[nPos].bTaught := TRUE;
END_IF
END_METHOD

METHOD _AxisValue : LREAL
VAR_INPUT
    nObject : UINT;
    fDefault : LREAL;
END_VAR
IF nObject >= 1 AND nObject <= GVL_Const.MAX_OBJ_PER_STATION AND_THEN aObj[nObject] <> 0 THEN
    _AxisValue := aObj[nObject].Status.aValue[1];     // E1 d'un axe : position actuelle
ELSE
    _AxisValue := fDefault;
END_IF
END_METHOD

// ---------------------------------------------------------------- variables (copie de travail pendant une calibration)

METHOD PRIVATE _ReadVar : LREAL
VAR_INPUT
    n : INT;
END_VAR
IF n < 1 OR n > GVL_Const.MAX_VARS THEN RETURN; END_IF
IF _bCalib AND _aWorkVarSet[n] THEN _ReadVar := _aWorkVar[n]; ELSE _ReadVar := aVar[n]; END_IF
END_METHOD

METHOD PRIVATE _ReadVarPos : ST_Position
VAR_INPUT
    n : INT;
END_VAR
IF n < 1 OR n > GVL_Const.MAX_VARS THEN RETURN; END_IF
IF _bCalib AND _aWorkVarSet[n] THEN _ReadVarPos := _aWorkVarPos[n]; ELSE _ReadVarPos := aVarPos[n]; END_IF
END_METHOD

METHOD PRIVATE _WriteVar
VAR_INPUT
    n : INT;
    fValue : LREAL;
END_VAR
IF n < 1 OR n > GVL_Const.MAX_VARS THEN RETURN; END_IF
IF _bCalib AND _refData.aVar[n].bRetain THEN
    IF NOT _aWorkVarSet[n] THEN _aWorkVarPos[n] := aVarPos[n]; END_IF
    _aWorkVar[n] := fValue;
    _aWorkVarSet[n] := TRUE;
ELSE
    aVar[n] := fValue;
END_IF
END_METHOD

METHOD PRIVATE _WriteVarPos
VAR_INPUT
    n : INT;
    stPos : ST_Position;
END_VAR
IF n < 1 OR n > GVL_Const.MAX_VARS THEN RETURN; END_IF
IF _bCalib AND _refData.aVar[n].bRetain THEN
    IF NOT _aWorkVarSet[n] THEN _aWorkVar[n] := aVar[n]; END_IF
    _aWorkVarPos[n] := stPos;
    _aWorkVarSet[n] := TRUE;
ELSE
    aVarPos[n] := stPos;
END_IF
END_METHOD

// ---------------------------------------------------------------- valeurs (docs/05 § 3.4)

METHOD PRIVATE _Resolve : LREAL
VAR_INPUT
    stVal : ST_Value;
END_VAR
VAR
    pPart : POINTER TO ST_Part;
    n : INT;
    k : INT;
END_VAR
n := stVal.nIndex;
CASE stVal.eSource OF
    E_ValueSource.Const:
        _Resolve := stVal.fValue;
    E_ValueSource.Var:
        _Resolve := _ReadVar(n);
    E_ValueSource.PartData:
        pPart := _CurrentPart();
        IF pPart <> 0 AND n >= 1 AND n <= GVL_Const.MAX_PART_DATA THEN _Resolve := pPart^.aData[n]; END_IF
    E_ValueSource.OpParam:
        IF n >= 1 AND n <= GVL_Const.MAX_PARAMS THEN _Resolve := _stReq.aParam[n]; END_IF
    E_ValueSource.MoverData:
        IF _pMover <> 0 AND n >= 1 AND n <= GVL_Const.MAX_MOVER_VARS THEN _Resolve := _pMover^.aVar[n]; END_IF
    E_ValueSource.Speed:
        _Resolve := INT_TO_LREAL(n);
    E_ValueSource.ObjValue:
        k := LREAL_TO_INT(stVal.fValue);
        IF n >= 1 AND n <= GVL_Const.MAX_OBJ_PER_STATION AND k >= 1 AND k <= GVL_Const.MAX_VALUES AND_THEN aObj[n] <> 0 THEN
            _Resolve := aObj[n].Status.aValue[k];
        END_IF
    E_ValueSource.System:
        CASE n OF
            1: _Resolve := BOOL_TO_LREAL(_bOpActive AND _pMover <> 0 AND _stReq.bMoverAtStation);
            2: _Resolve := USINT_TO_LREAL(_stReq.nSlot);
            3:
                pPart := _CurrentPart();
                IF pPart <> 0 THEN _Resolve := UDINT_TO_LREAL(pPart^.nOrder); END_IF
            4: _Resolve := UDINT_TO_LREAL(_nCycleCount);
            5: _Resolve := _fLastCycleMs;
        END_CASE
END_CASE
END_METHOD

// Vrai si la valeur est une position (position nommée, variable ou variable de mover de type position) ;
// stPos reçoit la position, le décalage fValue n'y est pas ajouté (l'objet l'ajoute à la composante utilisée).
METHOD PRIVATE _ResolvePos : BOOL
VAR_INPUT
    stVal : ST_Value;
END_VAR
VAR_OUTPUT
    stPos : ST_Position;
END_VAR
VAR
    n : INT;
END_VAR
n := stVal.nIndex;
_ResolvePos := FALSE;
CASE stVal.eSource OF
    E_ValueSource.NamedPos:
        IF n >= 1 AND n <= GVL_Const.MAX_NAMED_POS THEN
            stPos := _refData.aPos[n].stPos;
            IF _bCalib AND _aWorkPosSet[n] THEN stPos := _aWorkPos[n]; END_IF
            _ResolvePos := TRUE;
        END_IF
    E_ValueSource.Var:
        IF n >= 1 AND n <= GVL_Const.MAX_VARS AND_THEN _refData.aVar[n].eType = E_VarType.Position THEN
            stPos := _ReadVarPos(n);
            _ResolvePos := TRUE;
        END_IF
    E_ValueSource.MoverData:
        IF _pMover <> 0 AND n >= 1 AND n <= _refMachine.nMoverVar AND_THEN _refMachine.aMoverVar[n].eType = E_VarType.Position THEN
            stPos := _pMover^.aVarPos[n];
            _ResolvePos := TRUE;
        END_IF
END_CASE
END_METHOD

METHOD PRIVATE _ResolveParams
VAR_IN_OUT
    I : ST_Instr;
    stParams : ST_ActParams;
END_VAR
VAR
    k : USINT;
    stPos : ST_Position;
    n : INT;
END_VAR
MEMSET(ADR(stParams), 0, SIZEOF(stParams));
FOR k := 1 TO GVL_Const.MAX_PARAMS DO
    CASE I.aParam[k].eSource OF
        E_ValueSource.None:
            stParams.aKind[k] := E_ParamKind.Unused;
        E_ValueSource.Speed:
            IF I.aParam[k].nIndex > 0 THEN
                stParams.aKind[k] := E_ParamKind.SpeedIndex;
                stParams.aReal[k] := INT_TO_LREAL(I.aParam[k].nIndex);
            ELSE
                stParams.aKind[k] := E_ParamKind.SpeedPercent;
                stParams.aReal[k] := I.aParam[k].fValue;
            END_IF
        E_ValueSource.Text:
            n := I.aParam[k].nIndex;
            stParams.aKind[k] := E_ParamKind.Text;
            IF n >= 1 AND n <= GVL_Const.MAX_TEXTS THEN stParams.sText := _refData.aText[n]; END_IF
    ELSE
        IF _ResolvePos(I.aParam[k], stPos) THEN
            stParams.aKind[k] := E_ParamKind.Position;
            stParams.stPos := stPos;
            stParams.aReal[k] := I.aParam[k].fValue;        // décalage (hauteur d'approche)
        ELSE
            stParams.aKind[k] := E_ParamKind.Real;
            stParams.aReal[k] := _Resolve(I.aParam[k]);
        END_IF
    END_CASE
END_FOR
END_METHOD

// Rangement des résultats (docs/05 § 3.5)
METHOD PRIVATE _StoreResults
VAR_INPUT
    nCtx : USINT;
    ipObj : I_Object;
END_VAR
VAR
    I : REFERENCE TO ST_Instr;
    k : USINT;
    n : INT;
    pPart : POINTER TO ST_Part;
    bPos : BOOL;
END_VAR
I REF= _refData.aInstr[_aCtx[nCtx].nPC];
FOR k := 1 TO GVL_Const.MAX_PARAMS DO
    n := I.aBind[k].nIndex;
    bPos := ipObj.Results.nPosIndex = k;
    CASE I.aBind[k].eTarget OF
        E_BindTarget.Var:
            IF bPos THEN _WriteVarPos(n, ipObj.Results.stPos); ELSE _WriteVar(n, ipObj.Results.aReal[k]); END_IF
        E_BindTarget.PartData:
            pPart := _CurrentPart();
            IF pPart <> 0 AND n >= 1 AND n <= GVL_Const.MAX_PART_DATA THEN pPart^.aData[n] := ipObj.Results.aReal[k]; END_IF
        E_BindTarget.PartId:
            pPart := _CurrentPart();
            IF pPart <> 0 THEN pPart^.sPartId := ipObj.Results.sText; END_IF
        E_BindTarget.Verdict:
            IF ipObj.Results.aJudgement[k] = E_Judgement.Ok THEN
                _SetVerdict(E_Verdict.Good);
            ELSIF ipObj.Results.aJudgement[k] <> E_Judgement.None THEN
                _SetVerdict(E_Verdict.Bad);
            END_IF
        E_BindTarget.MoverData:
            IF _pMover <> 0 AND n >= 1 AND n <= GVL_Const.MAX_MOVER_VARS THEN
                IF bPos THEN _pMover^.aVarPos[n] := ipObj.Results.stPos; ELSE _pMover^.aVar[n] := ipObj.Results.aReal[k]; END_IF
            END_IF
        E_BindTarget.Output:
            _WriteOutput(n, 0, ipObj.Results.aReal[k], ipObj.Results.stPos, bPos);
    END_CASE
END_FOR
END_METHOD

METHOD PRIVATE _CurrentPart : POINTER TO ST_Part
VAR
    i : USINT;
END_VAR
_CurrentPart := 0;
IF NOT _bOpActive OR _pMover = 0 THEN RETURN; END_IF
IF _stReq.nSlot >= 1 AND _stReq.nSlot <= GVL_Const.MAX_SLOTS THEN
    _CurrentPart := ADR(_pMover^.aSlot[_stReq.nSlot]);
    RETURN;
END_IF
FOR i := 1 TO GVL_Const.MAX_SLOTS DO
    IF _pMover^.aSlot[i].bPresent THEN _CurrentPart := ADR(_pMover^.aSlot[i]); RETURN; END_IF
END_FOR
_CurrentPart := ADR(_pMover^.aSlot[1]);
END_METHOD

METHOD PRIVATE _SetVerdict
VAR_INPUT
    eVerdict : E_Verdict;
END_VAR
VAR
    pPart : POINTER TO ST_Part;
END_VAR
pPart := _CurrentPart();
IF pPart <> 0 THEN pPart^.aData[1] := TO_LREAL(eVerdict); END_IF
IF eVerdict = E_Verdict.Bad THEN _bOpBad := TRUE; END_IF
END_METHOD

METHOD PRIVATE _SetAlarmContext
VAR_INPUT
    nCtx : USINT;
    nAction : UINT;
END_VAR
VAR
    stCtx : ST_AlarmContext;
    pPart : POINTER TO ST_Part;
END_VAR
IF NOT __ISVALIDREF(_refAlarms) THEN RETURN; END_IF
stCtx.nTable := _aCtx[nCtx].nTable;
stCtx.nStepId := _aCtx[nCtx].nStepId;
stCtx.nMover := _stReq.nMover;
stCtx.nSlot := _stReq.nSlot;
stCtx.nAction := nAction;
pPart := _CurrentPart();
IF pPart <> 0 THEN stCtx.sPartId := pPart^.sPartId; END_IF
_refAlarms.SetContext(stCtx);
END_METHOD

// ---------------------------------------------------------------- conditions (docs/05 § 3.7)

METHOD PRIVATE _EvalCond : BOOL
VAR_INPUT
    n : INT;
END_VAR
VAR
    k : USINT;
    a : LREAL;
    b : LREAL;
    r : BOOL;
    bAll : BOOL;
END_VAR
IF n < 1 OR n > GVL_Const.MAX_COND THEN _EvalCond := TRUE; RETURN; END_IF
IF _refData.aCond[n].nTerms = 0 THEN _EvalCond := TRUE; RETURN; END_IF
bAll := _refData.aCond[n].eCombine = E_Combine.All;
FOR k := 1 TO MIN(_refData.aCond[n].nTerms, GVL_Const.MAX_COND_TERMS) DO
    a := _Resolve(_refData.aCond[n].aTerm[k].stLeft);
    b := _Resolve(_refData.aCond[n].aTerm[k].stRight);
    r := F_Compare(a, _refData.aCond[n].aTerm[k].eCmp, b);
    IF bAll AND NOT r THEN _EvalCond := FALSE; RETURN; END_IF
    IF NOT bAll AND r THEN _EvalCond := TRUE; RETURN; END_IF
END_FOR
_EvalCond := bAll;
END_METHOD

// ================================================================== verrouillages (I_InterlockProvider, docs/02 § 4.5)

METHOD Check : INT
VAR_INPUT
    nLocalId : UINT;
    nAction : UINT;
END_VAR
VAR
    i : INT;
    bApplies : BOOL;
    nType : UINT;
END_VAR
Check := 0;
FOR i := 1 TO GVL_Const.MAX_INTERLOCKS DO
    IF _refData.aInterlock[i].nObject = nLocalId AND nLocalId <> 0 THEN
        IF _refData.aInterlock[i].nActionMask = 0 THEN
            nType := 0;
            IF aObj[nLocalId] <> 0 THEN nType := aObj[nLocalId].Info.nType; END_IF
            bApplies := nType <= GVL_Const.MAX_TYPE AND_THEN F_CatalogBit(GVL_Catalog.aMotionMask[nType], nAction);
        ELSE
            bApplies := F_CatalogBit(_refData.aInterlock[i].nActionMask, nAction);
        END_IF
        IF bApplies AND NOT _EvalCond(_refData.aInterlock[i].nCond) THEN
            Check := i;
            RETURN;
        END_IF
    END_IF
END_FOR
END_METHOD

// ================================================================== publication (docs/05 § 4.5)

METHOD PRIVATE _Publish
VAR
    i : USINT;
    nNext : INT;
END_VAR
IF NOT __ISVALIDREF(_refStatus) THEN RETURN; END_IF
_refStatus.eState := fbPack.eState;
_refStatus.eMode := _eMode;
_refStatus.fEffOverride := LREAL_TO_REAL(EffectiveOverride);
_refStatus.bFault := _bFault;
_refStatus.bOutOfProduction := _bOutOfProduction;
_refStatus.bQualityLock := _bQualityLock;
_refStatus.nOpGroup := SEL(_bOpActive, 0, _stReq.nGroup);
_refStatus.nOpOperation := SEL(_bOpActive, 0, _stReq.nOperation);
_refStatus.nOpMover := SEL(_bOpActive, 0, _stReq.nMover);
_refStatus.nOpSlot := SEL(_bOpActive, 0, _stReq.nSlot);
_refStatus.eOpStatus := _stResp.eStatus;
_refStatus.fOpElapsedMs := LREAL_TO_REAL(_fOpMs);
_refStatus.bStepEnabled := __ISVALIDREF(_refStep) AND_THEN _refStep.bEnabled;
IF __ISVALIDREF(_refStep) THEN _refStatus.eStepGranularity := _refStep.eGranularity; END_IF
_refStatus.nCalib := _refData.nCalibCount;
_refStatus.aCalib := aCalib;
_refStatus.stPrompt := _stPrompt;
_refStatus.nCycleCount := _nCycleCount;
_refStatus.fLastCycleMs := LREAL_TO_REAL(_fLastCycleMs);
_refStatus.nBeacon := F_BeaconPattern(fbPack.eState, _eMode = E_Mode.Maintenance, _bQualityLock, FALSE);
FOR i := 1 TO GVL_Const.MAX_CTX DO
    _refStatus.aCtx[i].eStatus := _aCtx[i].eStatus;
    _refStatus.aCtx[i].nTable := _aCtx[i].nTable;
    _refStatus.aCtx[i].nPC := _aCtx[i].nPC;
    _refStatus.aCtx[i].nStepId := _aCtx[i].nStepId;
    _refStatus.aCtx[i].nRetry := _aCtx[i].nRetry;
    _refStatus.aCtx[i].fWaitMs := LREAL_TO_REAL(_aCtx[i].fWaitMs);
    _refStatus.aCtx[i].nObject := 0;
    _refStatus.aCtx[i].nAction := 0;
    _refStatus.aCtx[i].nNextStepId := 0;
    IF _aCtx[i].eStatus <> E_CtxStatus.Free AND _aCtx[i].nPC >= 1 AND _aCtx[i].nPC <= _refData.nInstrCount THEN
        IF _refData.aInstr[_aCtx[i].nPC].eOp = E_Op.Action THEN
            _refStatus.aCtx[i].nObject := _refData.aInstr[_aCtx[i].nPC].nObject;
            _refStatus.aCtx[i].nAction := _refData.aInstr[_aCtx[i].nPC].nAction;
            IF _aCtx[i].eStatus = E_CtxStatus.WaitStep THEN _ResolveParams(_refData.aInstr[_aCtx[i].nPC], _refStatus.aCtx[i].stNextParams); END_IF
        END_IF
        // carte suivante prévue : prochain StepBegin de niveau 1
        FOR nNext := _aCtx[i].nPC + 1 TO _refData.nInstrCount DO
            IF _refData.aInstr[nNext].eOp = E_Op.StepBegin AND _refData.aInstr[nNext].aArg[1] = 1 THEN
                _refStatus.aCtx[i].nNextStepId := _refData.aInstr[nNext].nStepId;
                EXIT;
            END_IF
        END_FOR
    END_IF
END_FOR
END_METHOD

// ================================================================== accès

PROPERTY State : E_State
GET
State := fbPack.eState;
END_GET
END_PROPERTY

PROPERTY Mode : E_Mode
GET
Mode := _eMode;
END_GET
END_PROPERTY

PROPERTY Response : REFERENCE TO ST_OpResponse
GET
Response REF= _stResp;
END_GET
END_PROPERTY

PROPERTY OperationActive : BOOL
GET
OperationActive := _bOpActive;
END_GET
END_PROPERTY

PROPERTY CurrentReqId : UDINT
GET
CurrentReqId := _stReq.nReqId;
END_GET
END_PROPERTY

PROPERTY Removed : BOOL
GET
Removed := _bRemoved;
END_GET
END_PROPERTY

PROPERTY MachineFault : BOOL
GET
MachineFault := _bMachineFault;
END_GET
END_PROPERTY

PROPERTY OutOfProduction : BOOL
GET
OutOfProduction := _bOutOfProduction;
END_GET
END_PROPERTY

PROPERTY QualityLock : BOOL
GET
QualityLock := _bQualityLock;
END_GET
END_PROPERTY

PROPERTY Data : REFERENCE TO ST_StationData
GET
Data REF= _refData;
END_GET
END_PROPERTY

PROPERTY Alarms : REFERENCE TO FB_AlarmManager
GET
Alarms REF= _refAlarms;
END_GET
END_PROPERTY
END_FUNCTION_BLOCK
