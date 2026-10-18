// Base de tous les objets élémentaires (docs/04 § 7) : identité, contrat d'action, timeout recalculé par
// l'override, verrouillages, simulation, alarmes, compteurs de maintenance, publication de l'état.
// Les types dérivés redéfinissent les méthodes protégées « _… » ; les valeurs par défaut viennent du catalogue.
FUNCTION_BLOCK FB_ObjectBase IMPLEMENTS I_Object, I_Maintained
VAR
    _stInfo : ST_ObjectInfo;
    _refCfg : REFERENCE TO ST_ObjConfig;
    _ipParent : I_Node;
    _ipLocks : I_InterlockProvider;
    _ipAlarms : I_AlarmSink;
    _eState : E_ObjState := E_ObjState.NotInitialized;
    _eActStatus : E_ActStatus := E_ActStatus.Idle;
    _nAction : UINT;
    _stParams : ST_ActParams;
    _stResults : ST_ActResults;
    _stStatus : ST_ObjStatus;
    _bManual : BOOL;
    _fProgressMs : LREAL;           // temps d'action compté au prorata de l'override (actions de mouvement)
    _nTimeoutMs : UDINT;
    _fElapsedMs : LREAL;            // durée réelle de l'action
    _fEffOverride : LREAL := 100.0;
    _nErrorCode : UINT;
    _nSubCode : UDINT;
    _bInitialized : BOOL;           // posé par le type dérivé à la fin de son Init
    _eFaultSeverity : E_Severity := E_Severity.StationFault;
    _aForced : ARRAY[1..GVL_Const.MAX_SIGNALS] OF BOOL;     // simulation : entrée forcée par la HMI
    _aForcedValue : ARRAY[1..GVL_Const.MAX_SIGNALS] OF LREAL;
    _bNewObject : BOOL;             // Configure : l'élément de pool change d'objet (valeurs persistantes du type à remettre à zéro)
END_VAR
VAR PERSISTENT
    _nStableId : UDINT;             // objet du projet auquel appartiennent ces compteurs (D92)
    _nActionCount : UDINT;
    _nErrorCount : UDINT;
    _fMotionHours : LREAL;
END_VAR

METHOD Configure : BOOL
VAR_INPUT
    refCfg : REFERENCE TO ST_ObjConfig;
    nStation : UINT;
    nPoolIndex : UINT;
    ipParent : I_Node;
    ipLocks : I_InterlockProvider;
    ipAlarms : I_AlarmSink;
END_VAR
_refCfg REF= refCfg;
_ipParent := ipParent;
_ipLocks := ipLocks;
_ipAlarms := ipAlarms;
_stInfo.nStation := nStation;
_stInfo.nLocalId := refCfg.nLocalId;
_stInfo.nParentId := refCfg.nParentId;
_stInfo.nType := refCfg.nType;
_stInfo.nPoolIndex := nPoolIndex;
_stInfo.nStableId := refCfg.nStableId;
_stInfo.sName := refCfg.sName;
_stInfo.nGlobalId := UINT_TO_UDINT(nStation) * 1000 + UINT_TO_UDINT(refCfg.nLocalId);
_eFaultSeverity := SEL(nStation = 0, E_Severity.StationFault, E_Severity.MachineFault);

// Un élément de pool repris par un autre objet repart de compteurs nuls (D92)
_bNewObject := _nStableId <> refCfg.nStableId;
IF _bNewObject THEN
    _nStableId := refCfg.nStableId;
    _nActionCount := 0;
    _nErrorCount := 0;
    _fMotionHours := 0;
END_IF

_eActStatus := E_ActStatus.Idle;
_nAction := 0;
_nErrorCode := 0;
_nSubCode := 0;
_bManual := FALSE;
_bInitialized := FALSE;
_fEffOverride := 100.0;
MEMSET(ADR(_aForced), 0, SIZEOF(_aForced));
MEMSET(ADR(_stResults), 0, SIZEOF(_stResults));

IF NOT _OnConfigure() THEN
    _eState := E_ObjState.Error;
    _nErrorCode := GVL_Catalog.Err_NotWired;
    Configure := FALSE;
    RETURN;
END_IF
IF refCfg.bDisabled THEN
    _eState := E_ObjState.Disabled;
ELSIF _NeedsInit() THEN
    _eState := E_ObjState.NotInitialized;
ELSE
    _eState := E_ObjState.Ready;
END_IF
Configure := TRUE;
END_METHOD

METHOD Start : E_StartResult
VAR_INPUT
    nAction : UINT;
    refParams : REFERENCE TO ST_ActParams;
    bManual : BOOL;
END_VAR
VAR
    nLock : INT;
    nCfgTimeout : UDINT;
END_VAR
IF NOT __ISVALIDREF(_refCfg) THEN Start := E_StartResult.NotConfigured; RETURN; END_IF
IF _eState = E_ObjState.Disabled THEN Start := E_StartResult.Disabled; RETURN; END_IF
IF _eState = E_ObjState.Busy THEN Start := E_StartResult.Busy; RETURN; END_IF
IF NOT F_CatalogHasAction(_stInfo.nType, nAction) THEN Start := E_StartResult.UnknownAction; RETURN; END_IF
IF _IsManualOnly(nAction) AND NOT bManual THEN Start := E_StartResult.ManualOnly; RETURN; END_IF
IF _eState = E_ObjState.Error THEN Start := E_StartResult.NotReady; RETURN; END_IF
IF _eState = E_ObjState.NotInitialized AND NOT _IsAllowedNotInit(nAction) THEN
    Start := E_StartResult.NotReady;
    RETURN;
END_IF

_stParams := refParams;
IF NOT _ValidateParams(nAction) THEN Start := E_StartResult.BadParam; RETURN; END_IF

IF _ipLocks <> 0 THEN
    nLock := _ipLocks.Check(_stInfo.nLocalId, nAction);
    IF nLock <> 0 THEN
        _stStatus.nInterlock := INT_TO_USINT(nLock);
        Start := E_StartResult.Interlocked;
        RETURN;
    END_IF
END_IF
_stStatus.nInterlock := 0;

// Acceptée
_nAction := nAction;
_bManual := bManual;
_fProgressMs := 0;
_fElapsedMs := 0;
IF nAction <= GVL_Const.MAX_ACTION_TIMEOUTS THEN nCfgTimeout := _refCfg.aTimeoutMs[nAction]; ELSE nCfgTimeout := 0; END_IF
IF _stParams.nTimeoutMs > 0 THEN
    _nTimeoutMs := _stParams.nTimeoutMs;
ELSIF nCfgTimeout > 0 THEN
    _nTimeoutMs := nCfgTimeout;
ELSE
    _nTimeoutMs := _DefaultTimeoutMs(nAction);
END_IF
MEMSET(ADR(_stResults), 0, SIZEOF(_stResults));
_nErrorCode := 0;
_nSubCode := 0;
_nActionCount := _nActionCount + 1;
_eState := E_ObjState.Busy;
_eActStatus := _OnStart(nAction);       // Busy, ou Done / Error immédiat
IF _eActStatus <> E_ActStatus.Busy THEN
    _EndAction();
END_IF
Start := E_StartResult.Accepted;
END_METHOD

METHOD Cycle
VAR
    fCycleMs : LREAL;
    fParentOv : LREAL;
    nLock : INT;
END_VAR
IF NOT __ISVALIDREF(_refCfg) THEN RETURN; END_IF
fCycleMs := F_CycleMs();

// 1. Override effectif : les parents sont appelés avant leurs enfants (docs/04 § 8)
fParentOv := 100.0;
IF _ipParent <> 0 THEN fParentOv := _ipParent.EffectiveOverride; END_IF
_fEffOverride := fParentOv * LIMIT(0.0, _refCfg.fOverride, 100.0) / 100.0;
IF _bManual AND _eState = E_ObjState.Busy THEN
    _fEffOverride := _fEffOverride * _ManualOverride() / 100.0;
END_IF
_ApplyOverride(_fEffOverride);

// 2. Entrées
IF _IsSimulated() THEN _Simulate(); ELSE _ReadHw(); END_IF

// 3. Action en cours
IF _eState = E_ObjState.Busy THEN
    _fElapsedMs := _fElapsedMs + fCycleMs;
    IF _IsMotion(_nAction) THEN
        _fProgressMs := _fProgressMs + fCycleMs * _fEffOverride / 100.0;
        _fMotionHours := _fMotionHours + fCycleMs / 3600000.0;
        IF _ipLocks <> 0 THEN
            nLock := _ipLocks.Check(_stInfo.nLocalId, _nAction);
            IF nLock <> 0 THEN
                _OnAbort(_nAction);
                _stStatus.nInterlock := INT_TO_USINT(nLock);
                _Fail(GVL_Catalog.Err_Interlocked, INT_TO_UDINT(nLock));
            END_IF
        END_IF
    ELSE
        _fProgressMs := _fProgressMs + fCycleMs;
    END_IF
    IF _eState = E_ObjState.Busy THEN
        _eActStatus := _OnCycle(_nAction);
        IF _eActStatus = E_ActStatus.Busy AND _nTimeoutMs > 0 AND _fProgressMs >= UDINT_TO_LREAL(_nTimeoutMs) THEN
            _OnAbort(_nAction);
            _Fail(GVL_Catalog.Err_Timeout, _nAction);
        ELSIF _eActStatus <> E_ActStatus.Busy THEN
            _EndAction();
        END_IF
    END_IF
END_IF

// 4. Surveillance continue
IF _eState <> E_ObjState.Disabled THEN _Monitor(); END_IF

// 5. Sorties
IF NOT _IsSimulated() THEN _WriteHw(); END_IF

// 6. Publication pour la HMI
_stStatus.eState := _eState;
_stStatus.eActStatus := _eActStatus;
_stStatus.nAction := UINT_TO_USINT(_nAction);
_stStatus.bManual := _bManual;
_stStatus.nErrorCode := _nErrorCode;
_stStatus.nSubCode := _nSubCode;
_stStatus.fEffOverride := LREAL_TO_REAL(_fEffOverride);
_stStatus.bOverrideHalted := (_eState = E_ObjState.Busy) AND _IsMotion(_nAction) AND (_fEffOverride <= 0.0);
_stStatus.bSimulated := _IsSimulated();
_stStatus.nElapsedMs := LREAL_TO_UDINT(_fElapsedMs);
_stStatus.nTimeoutMs := _EffectiveTimeoutEstimate();
_stStatus.nActionCount := _nActionCount;
_stStatus.nErrorCount := _nErrorCount;
_stStatus.fMotionHours := LREAL_TO_REAL(_fMotionHours);
_stStatus.fProgress := -1;
_Publish();                             // aValue[1..4], fProgress
END_METHOD

METHOD Abort
IF _eState = E_ObjState.Busy THEN
    _OnAbort(_nAction);
    _eActStatus := E_ActStatus.Error;
    _nErrorCode := GVL_Catalog.Err_Aborted;     // action interrompue par Abort : pas d'alarme levée
    _nSubCode := _nAction;
    _eState := SEL(_NeedsInit() AND NOT _bInitialized, E_ObjState.Ready, E_ObjState.NotInitialized);
END_IF
_SafeState();
IF _HardwareFault() AND _eState <> E_ObjState.Disabled THEN _eState := E_ObjState.Error; END_IF
END_METHOD

METHOD Ack : BOOL
IF _eState <> E_ObjState.Error THEN
    Ack := TRUE;
    RETURN;
END_IF
IF _OnAck() THEN
    IF _ipAlarms <> 0 AND _nErrorCode <> 0 THEN _ipAlarms.Clear(_stInfo.nGlobalId, _nErrorCode); END_IF
    _nErrorCode := 0;
    _nSubCode := 0;
    _eActStatus := E_ActStatus.Idle;
    _eState := SEL(_NeedsInit() AND NOT _bInitialized, E_ObjState.Ready, E_ObjState.NotInitialized);
    Ack := TRUE;
END_IF
END_METHOD

METHOD ForceInput
VAR_INPUT
    nSignal : USINT;
    fValue : LREAL;
END_VAR
IF nSignal = 0 THEN
    MEMSET(ADR(_aForced), 0, SIZEOF(_aForced));
ELSIF nSignal <= GVL_Const.MAX_SIGNALS THEN
    _aForced[nSignal] := TRUE;
    _aForcedValue[nSignal] := fValue;
END_IF
END_METHOD

PROPERTY EffectiveOverride : LREAL
GET
EffectiveOverride := _fEffOverride;
END_GET
END_PROPERTY

PROPERTY NodeState : E_ObjState
GET
NodeState := _eState;
END_GET
END_PROPERTY

PROPERTY Info : REFERENCE TO ST_ObjectInfo
GET
Info REF= _stInfo;
END_GET
END_PROPERTY

PROPERTY ActStatus : E_ActStatus
GET
ActStatus := _eActStatus;
END_GET
END_PROPERTY

PROPERTY Results : REFERENCE TO ST_ActResults
GET
Results REF= _stResults;
END_GET
END_PROPERTY

PROPERTY Status : REFERENCE TO ST_ObjStatus
GET
Status REF= _stStatus;
END_GET
END_PROPERTY

// ---------- Compteurs de maintenance (I_Maintained)

PROPERTY StableId : UDINT
GET
StableId := _nStableId;
END_GET
END_PROPERTY

PROPERTY ActionCount : UDINT
GET
ActionCount := _nActionCount;
END_GET
END_PROPERTY

PROPERTY ErrorCount : UDINT
GET
ErrorCount := _nErrorCount;
END_GET
END_PROPERTY

PROPERTY MotionHours : LREAL
GET
MotionHours := _fMotionHours;
END_GET
END_PROPERTY

// Restauration des valeurs sauvegardées par la HMI (docs/05 § 10, étape 6)
METHOD RestoreCounters
VAR_INPUT
    nActions : UDINT;
    nErrors : UDINT;
    fHours : LREAL;
END_VAR
_nActionCount := nActions;
_nErrorCount := nErrors;
_fMotionHours := fHours;
END_METHOD

// ---------- Fin d'action et erreurs

METHOD PROTECTED _EndAction
IF _eActStatus = E_ActStatus.Done THEN
    _eState := SEL(_NeedsInit() AND NOT _bInitialized, E_ObjState.Ready, E_ObjState.NotInitialized);
ELSIF _eActStatus = E_ActStatus.Error AND _eState <> E_ObjState.Error THEN
    _Fail(GVL_Catalog.Err_UnknownAction, _nAction);     // le type a répondu Error sans dire pourquoi
END_IF
END_METHOD

METHOD PROTECTED _Fail
VAR_INPUT
    nCode : UINT;
    nSubCode : UDINT;
END_VAR
_eActStatus := E_ActStatus.Error;
_nErrorCode := nCode;
_nSubCode := nSubCode;
_eState := E_ObjState.Error;
_nErrorCount := _nErrorCount + 1;
IF _ipAlarms <> 0 THEN _ipAlarms.Raise(_stInfo.nGlobalId, nCode, nSubCode, _eFaultSeverity); END_IF
END_METHOD

METHOD PROTECTED _SetResult
VAR_INPUT
    nIndex : USINT;
    fValue : LREAL;
END_VAR
IF nIndex >= 1 AND nIndex <= GVL_Const.MAX_PARAMS THEN
    _stResults.aReal[nIndex] := fValue;
    _stResults.nCount := MAX(_stResults.nCount, nIndex);
END_IF
END_METHOD

METHOD PROTECTED _SetJudgement
VAR_INPUT
    nIndex : USINT;
    eJudgement : E_Judgement;
END_VAR
IF nIndex >= 1 AND nIndex <= GVL_Const.MAX_PARAMS THEN
    _stResults.aJudgement[nIndex] := eJudgement;
    _stResults.aReal[nIndex] := TO_LREAL(eJudgement);
    _stResults.nCount := MAX(_stResults.nCount, nIndex);
END_IF
END_METHOD

METHOD PROTECTED _SetResultPos
VAR_INPUT
    nIndex : USINT;
    stPos : ST_Position;
END_VAR
_stResults.stPos := stPos;
_stResults.nPosIndex := nIndex;
_stResults.nCount := MAX(_stResults.nCount, nIndex);
END_METHOD

// ---------- Aides

METHOD PROTECTED _IsSimulated : BOOL
_IsSimulated := GVL_Sim.bAllSimulated OR (__ISVALIDREF(_refCfg) AND_THEN _refCfg.bSimulated);
END_METHOD

METHOD PROTECTED _Forced : BOOL
VAR_INPUT
    nSignal : USINT;
END_VAR
_Forced := nSignal >= 1 AND nSignal <= GVL_Const.MAX_SIGNALS AND_THEN _aForced[nSignal];
END_METHOD

METHOD PROTECTED _ForcedValue : LREAL
VAR_INPUT
    nSignal : USINT;
END_VAR
IF nSignal >= 1 AND nSignal <= GVL_Const.MAX_SIGNALS THEN _ForcedValue := _aForcedValue[nSignal]; END_IF
END_METHOD

METHOD PROTECTED _Param : LREAL
VAR_INPUT
    nParam : USINT;              // P1..P24
END_VAR
IF nParam >= 1 AND nParam <= GVL_Const.MAX_OBJ_PARAMS THEN _Param := _refCfg.aParam[nParam]; END_IF
END_METHOD

METHOD PROTECTED _ParamReal : LREAL
VAR_INPUT
    nParam : USINT;              // a1..a8
    fDefault : LREAL;
END_VAR
IF nParam >= 1 AND nParam <= GVL_Const.MAX_PARAMS AND_THEN _stParams.aKind[nParam] <> E_ParamKind.Unused THEN
    _ParamReal := _stParams.aReal[nParam];
ELSE
    _ParamReal := fDefault;
END_IF
END_METHOD

METHOD PROTECTED _EffectiveTimeoutEstimate : UDINT
IF _nTimeoutMs = 0 THEN
    _EffectiveTimeoutEstimate := 0;
ELSIF _IsMotion(_nAction) AND _fEffOverride <= 0.0 THEN
    _EffectiveTimeoutEstimate := 16#FFFFFFFF;          // « infini » à 0 %
ELSIF _IsMotion(_nAction) THEN
    _EffectiveTimeoutEstimate := LREAL_TO_UDINT(MIN(UDINT_TO_LREAL(_nTimeoutMs) * 100.0 / _fEffOverride, 86400000.0));
ELSE
    _EffectiveTimeoutEstimate := _nTimeoutMs;
END_IF
END_METHOD

// ---------- Méthodes à redéfinir par les types (valeurs par défaut tirées du catalogue)

METHOD PROTECTED _OnConfigure : BOOL
_OnConfigure := TRUE;
END_METHOD

METHOD PROTECTED _NeedsInit : BOOL
_NeedsInit := FALSE;
END_METHOD

METHOD PROTECTED _IsMotion : BOOL
VAR_INPUT
    nAction : UINT;
END_VAR
_IsMotion := _stInfo.nType <= GVL_Const.MAX_TYPE AND_THEN F_CatalogBit(GVL_Catalog.aMotionMask[_stInfo.nType], nAction);
END_METHOD

METHOD PROTECTED _IsAllowedNotInit : BOOL
VAR_INPUT
    nAction : UINT;
END_VAR
_IsAllowedNotInit := FALSE;
END_METHOD

METHOD PROTECTED _IsManualOnly : BOOL
VAR_INPUT
    nAction : UINT;
END_VAR
_IsManualOnly := _stInfo.nType <= GVL_Const.MAX_TYPE AND_THEN F_CatalogBit(GVL_Catalog.aManualOnlyMask[_stInfo.nType], nAction);
END_METHOD

METHOD PROTECTED _ValidateParams : BOOL
VAR_INPUT
    nAction : UINT;
END_VAR
_ValidateParams := TRUE;
END_METHOD

METHOD PROTECTED _DefaultTimeoutMs : UDINT
VAR_INPUT
    nAction : UINT;
END_VAR
IF _stInfo.nType <= GVL_Const.MAX_TYPE AND nAction >= 1 AND nAction <= GVL_Const.MAX_ACTION THEN
    _DefaultTimeoutMs := GVL_Catalog.aDefaultTimeoutMs[_stInfo.nType, nAction];
ELSE
    _DefaultTimeoutMs := 0;
END_IF
END_METHOD

METHOD PROTECTED _OnStart : E_ActStatus
VAR_INPUT
    nAction : UINT;
END_VAR
_OnStart := E_ActStatus.Error;
END_METHOD

METHOD PROTECTED _OnCycle : E_ActStatus
VAR_INPUT
    nAction : UINT;
END_VAR
_OnCycle := E_ActStatus.Done;
END_METHOD

METHOD PROTECTED _OnAbort
VAR_INPUT
    nAction : UINT;
END_VAR
;
END_METHOD

METHOD PROTECTED _SafeState
;
END_METHOD

METHOD PROTECTED _Monitor
;
END_METHOD

METHOD PROTECTED _OnAck : BOOL
_OnAck := TRUE;
END_METHOD

METHOD PROTECTED _HardwareFault : BOOL
_HardwareFault := FALSE;
END_METHOD

METHOD PROTECTED _ReadHw
;
END_METHOD

METHOD PROTECTED _WriteHw
;
END_METHOD

METHOD PROTECTED _Simulate
;
END_METHOD

METHOD PROTECTED _Publish
;
END_METHOD

METHOD PROTECTED _ApplyOverride
VAR_INPUT
    fPercent : LREAL;
END_VAR
;
END_METHOD

METHOD PROTECTED _ManualOverride : LREAL
_ManualOverride := 100.0;
END_METHOD
END_FUNCTION_BLOCK
