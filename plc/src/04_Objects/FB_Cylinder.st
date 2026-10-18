// Vérin, type 10 (docs/03 § 4.1, exemple complet de docs/04 § 11)
FUNCTION_BLOCK FB_Cylinder EXTENDS FB_ObjectBase
VAR
    _bBistable : BOOL;
    _tSettleMs : LREAL;
    _tNoSensorMs : LREAL;
    _eSafe : USINT;                 // 0 Repos, 1 Travail, 2 Maintien
    _bCmdWork : BOOL;               // sortie travail commandée
    _bAtWork : BOOL;                // capteurs (lus ou simulés)
    _bAtRest : BOOL;
    _bHasWorkSensor : BOOL;
    _bHasRestSensor : BOOL;
    _fSettle : LREAL;
    _fMove : LREAL;
    _fSimPos : LREAL;               // simulation : 0 = repos, 1 = travail
    _nReached : USINT;              // 0 aucune, 1 repos, 2 travail : dernière position atteinte par une action
END_VAR

METHOD PROTECTED _OnConfigure : BOOL
_bBistable := _refCfg.aParam[1] = 1;
_tSettleMs := LIMIT(0.0, _refCfg.aParam[2], 5000.0);
_tNoSensorMs := MAX(_refCfg.aParam[3], 10.0);
_eSafe := LREAL_TO_USINT(_refCfg.aParam[4]);
_bHasWorkSensor := F_SignalValid(_refCfg.aSignal[3], E_SignalKind.DI);
_bHasRestSensor := F_SignalValid(_refCfg.aSignal[4], E_SignalKind.DI);
_nReached := 0;
_OnConfigure := F_SignalValid(_refCfg.aSignal[1], E_SignalKind.DO)
                AND (NOT _bBistable OR F_SignalValid(_refCfg.aSignal[2], E_SignalKind.DO));
END_METHOD

METHOD PROTECTED _DefaultTimeoutMs : UDINT
VAR_INPUT
    nAction : UINT;
END_VAR
_DefaultTimeoutMs := SEL(_bHasWorkSensor OR _bHasRestSensor, LREAL_TO_UDINT(_tNoSensorMs) + 500, 2000);
END_METHOD

METHOD PROTECTED _OnStart : E_ActStatus
VAR_INPUT
    nAction : UINT;
END_VAR
CASE nAction OF
    GVL_Catalog.Cylinder_MoveTravail, GVL_Catalog.Cylinder_MoveRepos:
        _fMove := 0;
        _fSettle := 0;
        _nReached := 0;
        _OnStart := E_ActStatus.Busy;   // la commande est posée dans _OnCycle (attente si override 0 %)
    GVL_Catalog.Cylinder_LirePosition:
        _SetResult(1, _PositionCode());
        _OnStart := E_ActStatus.Done;
ELSE
    _OnStart := E_ActStatus.Error;
END_CASE
END_METHOD

METHOD PROTECTED _OnCycle : E_ActStatus
VAR_INPUT
    nAction : UINT;
END_VAR
VAR
    bTarget : BOOL;
    bSensorOk : BOOL;
    fCycleMs : LREAL;
END_VAR
fCycleMs := F_CycleMs();
IF _fEffOverride <= 0 AND _fMove = 0 THEN _OnCycle := E_ActStatus.Busy; RETURN; END_IF   // 0 % : on attend
bTarget := nAction = GVL_Catalog.Cylinder_MoveTravail;
_bCmdWork := bTarget;
_fMove := _fMove + fCycleMs;
// Capteur de départ toujours actif après 500 ms : vérin bloqué (erreur 102)
IF _fMove >= 500.0 AND ((bTarget AND _bHasRestSensor AND _bAtRest) OR (NOT bTarget AND _bHasWorkSensor AND _bAtWork)) THEN
    _Fail(GVL_Catalog.Cylinder_Err102, 0);
    _OnCycle := E_ActStatus.Error;
    RETURN;
END_IF
IF bTarget THEN
    bSensorOk := SEL(_bHasWorkSensor, _fMove >= _tNoSensorMs, _bAtWork AND NOT _bAtRest);
ELSE
    bSensorOk := SEL(_bHasRestSensor, _fMove >= _tNoSensorMs, _bAtRest AND NOT _bAtWork);
END_IF
IF bSensorOk THEN _fSettle := _fSettle + fCycleMs; ELSE _fSettle := 0; END_IF
IF _fSettle >= _tSettleMs THEN
    _nReached := SEL(bTarget, 1, 2);
    _OnCycle := E_ActStatus.Done;
ELSE
    _OnCycle := E_ActStatus.Busy;
END_IF
END_METHOD

METHOD PROTECTED _SafeState
CASE _eSafe OF
    0: _bCmdWork := FALSE;
    1: _bCmdWork := TRUE;
END_CASE                            // 2 : maintien
END_METHOD

METHOD PROTECTED _Monitor
IF _bAtWork AND _bAtRest THEN
    IF _eState <> E_ObjState.Error THEN _Fail(GVL_Catalog.Cylinder_Err100, 0); END_IF
ELSIF _eState = E_ObjState.Ready THEN
    // Perte du capteur de la position atteinte hors mouvement (erreur 101)
    IF (_nReached = 2 AND _bHasWorkSensor AND NOT _bAtWork) OR (_nReached = 1 AND _bHasRestSensor AND NOT _bAtRest) THEN
        _Fail(GVL_Catalog.Cylinder_Err101, _nReached);
        _nReached := 0;
    END_IF
END_IF
END_METHOD

METHOD PROTECTED _ReadHw
_bAtWork := F_ReadDI(_refCfg.aSignal[3]);
_bAtRest := F_ReadDI(_refCfg.aSignal[4]);
END_METHOD

METHOD PROTECTED _WriteHw
F_WriteDO(_refCfg.aSignal[1], _bCmdWork);
IF _bBistable THEN F_WriteDO(_refCfg.aSignal[2], NOT _bCmdWork); END_IF
END_METHOD

METHOD PROTECTED _Simulate
VAR
    fStep : LREAL;
END_VAR
fStep := F_CycleMs() / SEL(_refCfg.aParam[3] > 0, 300.0, _tNoSensorMs);
IF _bCmdWork THEN _fSimPos := MIN(1.0, _fSimPos + fStep); ELSE _fSimPos := MAX(0.0, _fSimPos - fStep); END_IF
_bAtWork := _fSimPos >= 1.0;
_bAtRest := _fSimPos <= 0.0;
IF _Forced(3) THEN _bAtWork := _ForcedValue(3) >= 0.5; END_IF
IF _Forced(4) THEN _bAtRest := _ForcedValue(4) >= 0.5; END_IF
END_METHOD

METHOD PRIVATE _PositionCode : LREAL
// 0 Inconnue, 1 Repos, 2 Travail, 3 Intermédiaire, 4 Incohérente
IF _bAtWork AND _bAtRest THEN
    _PositionCode := 4;
ELSIF _bAtRest THEN
    _PositionCode := 1;
ELSIF _bAtWork THEN
    _PositionCode := 2;
ELSIF _bHasWorkSensor OR _bHasRestSensor THEN
    _PositionCode := 3;
ELSE
    _PositionCode := 0;
END_IF
END_METHOD

METHOD PROTECTED _Publish
_stStatus.aValue[1] := _PositionCode();
IF _eState = E_ObjState.Busy THEN
    _stStatus.fProgress := LREAL_TO_REAL(MIN(1.0, _fMove / MAX(_tNoSensorMs, 1.0)));
END_IF
END_METHOD
END_FUNCTION_BLOCK
