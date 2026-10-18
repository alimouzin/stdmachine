// Axe linéaire (type 11) et axe rotatif (type 12), docs/03 § 4.2 et docs/04 § 12.
// Matériel : D = n° de l'axe dans GVL_Hw.aAxis (pointeur donné par GVL_IoRef). Blocs Tc2_MC2.
// L'override effectif est appliqué par MC_Power.Override : il ralentit le mouvement en cours sans le relancer.
FUNCTION_BLOCK FB_Axis EXTENDS FB_ObjectBase
VAR
    _pAxis : POINTER TO AXIS_REF;
    _bRotary : BOOL;
    _fbPower : MC_Power;
    _fbReset : MC_Reset;
    _fbHome : MC_Home;
    _fbMoveAbs : MC_MoveAbsolute;
    _fbMoveMod : MC_MoveModulo;
    _fbMoveRel : MC_MoveRelative;
    _fbHalt : MC_Halt;
    _fbStop : MC_Stop;
    _fbJog : MC_Jog;
    _bEnable : BOOL;                // alimentation demandée
    _bPowered : BOOL;
    _bWasPowered : BOOL;
    _bHomed : BOOL;
    _fActPos : LREAL;
    _fActVel : LREAL;
    _fTarget : LREAL;
    _fStartPos : LREAL;
    _fVelo : LREAL;
    _fAcc : LREAL;
    _fDec : LREAL;
    _nDirection : INT;              // rotatif : 0 plus court, 1 positif, 2 négatif
    _bExecute : BOOL;               // front de commande de l'action en cours
    _bStopping : BOOL;              // Abort : arrêt en cours
    _fOverride : LREAL := 100.0;
    // Simulation : profil trapézoïdal
    _fSimPos : LREAL;
    _fSimVel : LREAL;
    _fSimInitMs : LREAL;
    _nJogDir : INT;                 // +1, -1, 0
END_VAR

METHOD PROTECTED _OnConfigure : BOOL
_bRotary := _refCfg.nType = GVL_Catalog.Type_RotaryAxis;
_pAxis := F_AxisRef(_refCfg.nDevice);
_bEnable := FALSE;
_bHomed := FALSE;
_fSimVel := 0;
_OnConfigure := _pAxis <> 0 OR _IsSimulated();
END_METHOD

METHOD PROTECTED _NeedsInit : BOOL
_NeedsInit := TRUE;
END_METHOD

METHOD PROTECTED _IsAllowedNotInit : BOOL
VAR_INPUT
    nAction : UINT;
END_VAR
_IsAllowedNotInit := nAction = GVL_Catalog.LinearAxis_PowerOn OR nAction = GVL_Catalog.LinearAxis_PowerOff
                     OR nAction = GVL_Catalog.LinearAxis_Init OR nAction = GVL_Catalog.LinearAxis_AcquitterErreur
                     OR nAction = GVL_Catalog.LinearAxis_LirePosition
                     OR nAction = GVL_Catalog.LinearAxis_JogPlus OR nAction = GVL_Catalog.LinearAxis_JogMoins;
END_METHOD

METHOD PROTECTED _ManualOverride : LREAL
_ManualOverride := LIMIT(0.0, _refCfg.aParam[12], 100.0);
END_METHOD

METHOD PROTECTED _DefaultTimeoutMs : UDINT
VAR_INPUT
    nAction : UINT;
END_VAR
VAR
    fDist : LREAL;
END_VAR
// Move absolu et relatif : 2 × (distance ÷ vitesse + vitesse ÷ accélération) × 1000 + 1000 ms, à 100 %
IF nAction = GVL_Catalog.LinearAxis_MoveAbsolu OR nAction = GVL_Catalog.LinearAxis_MoveRelatif THEN
    IF nAction = GVL_Catalog.LinearAxis_MoveAbsolu THEN fDist := ABS(_TargetOf() - _fActPos); ELSE fDist := ABS(_ParamReal(1, 0.0)); END_IF
    _Kinematics();
    IF _fVelo > 0 AND _fAcc > 0 THEN
        _DefaultTimeoutMs := LREAL_TO_UDINT(2.0 * (fDist / _fVelo + _fVelo / _fAcc) * 1000.0 + 1000.0);
    ELSE
        _DefaultTimeoutMs := 30000;
    END_IF
ELSE
    _DefaultTimeoutMs := SUPER^._DefaultTimeoutMs(nAction);
END_IF
END_METHOD

METHOD PRIVATE _TargetOf : LREAL
// Une position nommée : composante P13 (X, Y, Z) ou C pour un rotatif, plus le décalage (hauteur d'approche)
IF _stParams.aKind[1] = E_ParamKind.Position THEN
    IF _bRotary THEN
        _TargetOf := _stParams.stPos.fC;
    ELSE
        CASE LREAL_TO_INT(_refCfg.aParam[13]) OF
            1: _TargetOf := _stParams.stPos.fY;
            2: _TargetOf := _stParams.stPos.fZ;
        ELSE
            _TargetOf := _stParams.stPos.fX;
        END_CASE
    END_IF
    _TargetOf := _TargetOf + _stParams.aReal[1];
ELSE
    _TargetOf := _stParams.aReal[1];
END_IF
END_METHOD

METHOD PRIVATE _Kinematics
VAR
    fAccPct : LREAL;
END_VAR
_fVelo := MAX(_refCfg.aParam[1], 0.001) * F_ParamSpeed(_stParams, 2, _refCfg, 2) / 100.0;
fAccPct := LIMIT(1.0, _ParamReal(3, 100.0), 100.0);
IF _stParams.aKind[3] = E_ParamKind.Unused THEN fAccPct := 100.0; END_IF
_fAcc := MAX(_refCfg.aParam[2], 0.001) * fAccPct / 100.0;
_fDec := MAX(_refCfg.aParam[3], 0.001) * fAccPct / 100.0;
END_METHOD

METHOD PROTECTED _OnStart : E_ActStatus
VAR_INPUT
    nAction : UINT;
END_VAR
_bExecute := FALSE;
_bStopping := FALSE;
_fStartPos := _fActPos;
CASE nAction OF
    GVL_Catalog.LinearAxis_PowerOn:
        _bEnable := TRUE;
        _OnStart := E_ActStatus.Busy;
    GVL_Catalog.LinearAxis_PowerOff:
        _bEnable := FALSE;
        _OnStart := E_ActStatus.Busy;
    GVL_Catalog.LinearAxis_Init:
        IF NOT _bPowered THEN _Fail(GVL_Catalog.LinearAxis_Err105, 0); _OnStart := E_ActStatus.Error; RETURN; END_IF
        _bHomed := FALSE;
        _bInitialized := FALSE;
        _fSimInitMs := 0;
        _OnStart := E_ActStatus.Busy;
    GVL_Catalog.LinearAxis_MoveAbsolu, GVL_Catalog.LinearAxis_MoveRelatif:
        IF NOT _bPowered THEN _Fail(GVL_Catalog.LinearAxis_Err105, 0); _OnStart := E_ActStatus.Error; RETURN; END_IF
        IF NOT _bHomed THEN _Fail(GVL_Catalog.LinearAxis_Err100, 0); _OnStart := E_ActStatus.Error; RETURN; END_IF
        IF nAction = GVL_Catalog.LinearAxis_MoveAbsolu THEN
            _fTarget := _TargetOf();
            _nDirection := LREAL_TO_INT(_ParamReal(4, _refCfg.aParam[14]));
        ELSE
            _fTarget := _fActPos + _ParamReal(1, 0.0);
        END_IF
        IF _refCfg.aParam[7] = 1 AND (_fTarget < _refCfg.aParam[5] OR _fTarget > _refCfg.aParam[6]) THEN
            _Fail(GVL_Catalog.LinearAxis_Err101, 0);
            _OnStart := E_ActStatus.Error;
            RETURN;
        END_IF
        _Kinematics();
        _OnStart := E_ActStatus.Busy;
    GVL_Catalog.LinearAxis_Stop:
        _OnStart := E_ActStatus.Busy;
    GVL_Catalog.LinearAxis_AcquitterErreur:
        _OnStart := E_ActStatus.Busy;
    GVL_Catalog.LinearAxis_LirePosition:
        _SetResult(1, _fActPos);
        _OnStart := E_ActStatus.Done;
    GVL_Catalog.LinearAxis_JogPlus, GVL_Catalog.LinearAxis_JogMoins:
        IF NOT _bPowered THEN _Fail(GVL_Catalog.LinearAxis_Err105, 0); _OnStart := E_ActStatus.Error; RETURN; END_IF
        _fVelo := MAX(_refCfg.aParam[1], 0.001) * F_ParamSpeed(_stParams, 1, _refCfg, 1) / 100.0;
        IF NOT _bHomed THEN _fVelo := MAX(_refCfg.aParam[1], 0.001) * F_ParamSpeed(_stParams, 0, _refCfg, 1) / 100.0; END_IF   // V1 imposée
        _fAcc := MAX(_refCfg.aParam[2], 0.001);
        _fDec := MAX(_refCfg.aParam[3], 0.001);
        _nJogDir := SEL(nAction = GVL_Catalog.LinearAxis_JogPlus, -1, 1);
        _OnStart := E_ActStatus.Busy;       // le jog dure jusqu'à Abort (canal manuel, docs/05 § 7)
ELSE
    _OnStart := E_ActStatus.Error;
END_CASE
END_METHOD

METHOD PROTECTED _OnCycle : E_ActStatus
VAR_INPUT
    nAction : UINT;
END_VAR
_OnCycle := E_ActStatus.Busy;
CASE nAction OF
    GVL_Catalog.LinearAxis_PowerOn:
        IF _bPowered THEN _OnCycle := E_ActStatus.Done; END_IF
    GVL_Catalog.LinearAxis_PowerOff:
        IF NOT _bPowered THEN _OnCycle := E_ActStatus.Done; END_IF
    GVL_Catalog.LinearAxis_Init:
        IF _IsSimulated() THEN
            _fSimInitMs := _fSimInitMs + F_CycleMs();
            IF _fSimInitMs >= 1500.0 THEN
                _fSimPos := _refCfg.aParam[8];
                _fSimVel := 0;
                _bHomed := TRUE;
            END_IF
        ELSE
            _bExecute := TRUE;
            IF _fbHome.Done THEN _bHomed := TRUE; END_IF
            IF _fbHome.Error THEN _Fail(GVL_Catalog.LinearAxis_Err102, _fbHome.ErrorID); _OnCycle := E_ActStatus.Error; RETURN; END_IF
        END_IF
        IF _bHomed THEN
            _bInitialized := TRUE;
            _bExecute := FALSE;
            _OnCycle := E_ActStatus.Done;
        END_IF
    GVL_Catalog.LinearAxis_MoveAbsolu, GVL_Catalog.LinearAxis_MoveRelatif:
        IF _IsSimulated() THEN
            IF ABS(_fSimPos - _fTarget) <= MAX(_refCfg.aParam[4], 1.0E-6) AND ABS(_fSimVel) < 1.0E-6 THEN _OnCycle := E_ActStatus.Done; END_IF
        ELSE
            _bExecute := TRUE;
            IF _fbMoveAbs.Done OR _fbMoveRel.Done OR _fbMoveMod.Done THEN _OnCycle := E_ActStatus.Done; END_IF
            IF _fbMoveAbs.Error OR _fbMoveRel.Error OR _fbMoveMod.Error THEN
                _Fail(GVL_Catalog.LinearAxis_Err102, _fbMoveAbs.ErrorID OR _fbMoveRel.ErrorID OR _fbMoveMod.ErrorID);
                _OnCycle := E_ActStatus.Error;
                RETURN;
            END_IF
        END_IF
        IF _OnCycle = E_ActStatus.Done THEN
            _bExecute := FALSE;
            _SetResult(1, _fActPos);
        END_IF
    GVL_Catalog.LinearAxis_Stop:
        IF _IsSimulated() THEN
            IF ABS(_fSimVel) < 1.0E-6 THEN _OnCycle := E_ActStatus.Done; END_IF
        ELSE
            _bExecute := TRUE;
            IF _fbHalt.Done THEN _bExecute := FALSE; _OnCycle := E_ActStatus.Done; END_IF
        END_IF
    GVL_Catalog.LinearAxis_AcquitterErreur:
        IF _IsSimulated() THEN
            _OnCycle := E_ActStatus.Done;
        ELSE
            _bExecute := TRUE;
            IF _fbReset.Done OR _fbReset.Error THEN _bExecute := FALSE; _OnCycle := E_ActStatus.Done; END_IF
        END_IF
    GVL_Catalog.LinearAxis_JogPlus, GVL_Catalog.LinearAxis_JogMoins:
        ;                               // jusqu'à Abort
END_CASE
_CallMotion();
END_METHOD

METHOD PROTECTED _OnAbort
VAR_INPUT
    nAction : UINT;
END_VAR
// Arrêt avec la décélération maximale, puis P11 (0 alimentation maintenue, 1 coupée)
_bExecute := FALSE;
_nJogDir := 0;
_bStopping := TRUE;
END_METHOD

METHOD PROTECTED _SafeState
IF _refCfg.aParam[11] = 1 THEN _bEnable := FALSE; END_IF
END_METHOD

METHOD PROTECTED _OnAck : BOOL
IF _IsSimulated() OR _pAxis = 0 THEN
    _OnAck := TRUE;
    RETURN;
END_IF
_fbReset(Axis := _pAxis^, Execute := TRUE);
IF _fbReset.Done OR _fbReset.Error THEN
    _fbReset(Axis := _pAxis^, Execute := FALSE);
    _OnAck := TRUE;
END_IF
END_METHOD

METHOD PROTECTED _ApplyOverride
VAR_INPUT
    fPercent : LREAL;
END_VAR
_fOverride := LIMIT(0.0, fPercent, 100.0);
END_METHOD

METHOD PROTECTED _ReadHw
IF _pAxis = 0 THEN RETURN; END_IF
_pAxis^.ReadStatus();
_fActPos := _pAxis^.NcToPlc.ActPos;
_fActVel := _pAxis^.NcToPlc.ActVelo;
_bHomed := _bHomed OR _pAxis^.Status.Homed;
_fbPower(Axis := _pAxis^, Enable := _bEnable, Enable_Positive := _bEnable, Enable_Negative := _bEnable, Override := _fOverride);
_bPowered := _fbPower.Status;
IF NOT _bExecute AND _eState <> E_ObjState.Busy THEN _CallMotion(); END_IF    // remet les Execute à FALSE
END_METHOD

METHOD PRIVATE _CallMotion
VAR
    bAbs : BOOL;
    bRel : BOOL;
    bMod : BOOL;
    eDir : MC_Direction;
END_VAR
IF _IsSimulated() OR _pAxis = 0 THEN RETURN; END_IF
bAbs := _bExecute AND _nAction = GVL_Catalog.LinearAxis_MoveAbsolu AND NOT (_bRotary AND _refCfg.aParam[7] = 0);
bMod := _bExecute AND _nAction = GVL_Catalog.LinearAxis_MoveAbsolu AND _bRotary AND _refCfg.aParam[7] = 0;
bRel := _bExecute AND _nAction = GVL_Catalog.LinearAxis_MoveRelatif;
CASE _nDirection OF
    1: eDir := MC_Positive_Direction;
    2: eDir := MC_Negative_Direction;
ELSE
    eDir := MC_Shortest_Way;
END_CASE
_fbHome(Axis := _pAxis^, Execute := _bExecute AND _nAction = GVL_Catalog.LinearAxis_Init, Position := _refCfg.aParam[8],
        HomingMode := SEL(LREAL_TO_INT(_refCfg.aParam[9]) = 3, MC_DefaultHoming, MC_Direct));
_fbMoveAbs(Axis := _pAxis^, Execute := bAbs, Position := _fTarget, Velocity := _fVelo, Acceleration := _fAcc, Deceleration := _fDec);
_fbMoveMod(Axis := _pAxis^, Execute := bMod, Position := _fTarget, Velocity := _fVelo, Acceleration := _fAcc, Deceleration := _fDec, Direction := eDir);
_fbMoveRel(Axis := _pAxis^, Execute := bRel, Distance := _fTarget - _fStartPos, Velocity := _fVelo, Acceleration := _fAcc, Deceleration := _fDec);
_fbHalt(Axis := _pAxis^, Execute := _bExecute AND _nAction = GVL_Catalog.LinearAxis_Stop);
_fbReset(Axis := _pAxis^, Execute := _bExecute AND _nAction = GVL_Catalog.LinearAxis_AcquitterErreur);
_fbJog(Axis := _pAxis^, JogForward := _nJogDir > 0, JogBackwards := _nJogDir < 0, Mode := MC_JOGMODE_CONTINOUS,
       Velocity := _fVelo, Acceleration := _fAcc, Deceleration := _fDec);
_fbStop(Axis := _pAxis^, Execute := _bStopping);
IF _bStopping AND (_fbStop.Done OR _fbStop.Error) THEN
    _bStopping := FALSE;
    _fbStop(Axis := _pAxis^, Execute := FALSE);
END_IF
END_METHOD

METHOD PROTECTED _Monitor
IF _IsSimulated() OR _pAxis = 0 THEN
    _bWasPowered := _bPowered;
    RETURN;
END_IF
IF _pAxis^.Status.Error AND _eState <> E_ObjState.Error THEN
    IF _pAxis^.Status.ErrorID = 16#4550 OR _pAxis^.Status.ErrorID = 16#4551 THEN
        _Fail(GVL_Catalog.LinearAxis_Err103, _pAxis^.Status.ErrorID);      // erreur de suivi
    ELSE
        _Fail(GVL_Catalog.LinearAxis_Err102, _pAxis^.Status.ErrorID);      // erreur du drive (sous-code NC)
    END_IF
ELSIF _bWasPowered AND NOT _bPowered AND _bEnable AND _eState <> E_ObjState.Error THEN
    _Fail(GVL_Catalog.LinearAxis_Err104, 0);                                 // perte d'alimentation
END_IF
_bWasPowered := _bPowered;
END_METHOD

METHOD PROTECTED _Simulate
VAR
    fDt : LREAL;
    fVmax : LREAL;
    fDist : LREAL;
    fBrake : LREAL;
    fDir : LREAL;
END_VAR
fDt := F_CycleMs() / 1000.0;
_bPowered := _bEnable;
fVmax := _fVelo * _fOverride / 100.0;
IF _bStopping THEN
    // arrêt avec la décélération maximale
    IF _fSimVel > 0 THEN _fSimVel := MAX(0.0, _fSimVel - _refCfg.aParam[3] * fDt); ELSE _fSimVel := MIN(0.0, _fSimVel + _refCfg.aParam[3] * fDt); END_IF
    IF ABS(_fSimVel) < 1.0E-9 THEN _bStopping := FALSE; END_IF
ELSIF _eState = E_ObjState.Busy AND (_nAction = GVL_Catalog.LinearAxis_JogPlus OR _nAction = GVL_Catalog.LinearAxis_JogMoins) THEN
    _fSimVel := LIMIT(-fVmax, _fSimVel + INT_TO_LREAL(_nJogDir) * _fAcc * fDt, fVmax);
ELSIF _eState = E_ObjState.Busy AND (_nAction = GVL_Catalog.LinearAxis_MoveAbsolu OR _nAction = GVL_Catalog.LinearAxis_MoveRelatif) THEN
    fDist := _fTarget - _fSimPos;
    fDir := SEL(fDist >= 0, -1.0, 1.0);
    fBrake := _fSimVel * _fSimVel / (2.0 * MAX(_fDec, 0.001));
    IF ABS(fDist) <= fBrake THEN
        _fSimVel := _fSimVel - fDir * _fDec * fDt;          // décélération
        IF fDir * _fSimVel < 0 THEN _fSimVel := 0; END_IF
    ELSE
        _fSimVel := LIMIT(-fVmax, _fSimVel + fDir * _fAcc * fDt, fVmax);
    END_IF
    IF ABS(fDist) <= ABS(_fSimVel * fDt) OR (ABS(_fSimVel) < 1.0E-9 AND ABS(fDist) <= MAX(_refCfg.aParam[4], 1.0E-6)) THEN
        _fSimPos := _fTarget;
        _fSimVel := 0;
    END_IF
ELSIF _eState = E_ObjState.Busy AND _nAction = GVL_Catalog.LinearAxis_Stop THEN
    IF _fSimVel > 0 THEN _fSimVel := MAX(0.0, _fSimVel - _fDec * fDt); ELSE _fSimVel := MIN(0.0, _fSimVel + _fDec * fDt); END_IF
ELSE
    _fSimVel := 0;
END_IF
_fSimPos := _fSimPos + _fSimVel * fDt;
_fActPos := _fSimPos;
_fActVel := _fSimVel;
END_METHOD

METHOD PROTECTED _Publish
_stStatus.aValue[1] := _fActPos;
_stStatus.aValue[2] := _fActVel;
_stStatus.aValue[3] := BOOL_TO_LREAL(_bPowered);
_stStatus.aValue[4] := BOOL_TO_LREAL(_bHomed);
IF _eState = E_ObjState.Busy AND ABS(_fTarget - _fStartPos) > 1.0E-9
   AND (_nAction = GVL_Catalog.LinearAxis_MoveAbsolu OR _nAction = GVL_Catalog.LinearAxis_MoveRelatif) THEN
    _stStatus.fProgress := LREAL_TO_REAL(LIMIT(0.0, ABS(_fActPos - _fStartPos) / ABS(_fTarget - _fStartPos), 1.0));
END_IF
END_METHOD

PROPERTY ActualPosition : LREAL
GET
ActualPosition := _fActPos;
END_GET
END_PROPERTY
END_FUNCTION_BLOCK
