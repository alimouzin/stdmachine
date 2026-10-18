// Presse, type 40 (docs/03 § 7.1) : D axe NC de la presse, S1 AI capteur de force. Recettes déployées dans
// GVL_Active.aPressRecipe (8 segments au plus : course cible ou force cible, vitesse, fenêtres de contrôle).
// Chaque exécution enregistre la courbe force/course (1 000 points au plus) dans GVL_Hmi.aCurve.
// Hypothèses : la valeur brute de S1 est en newtons ; la position de repos est la course 0 de l'axe (la fiche
// parle d'une position nommée sans dire laquelle) ; l'axe est référencé par TwinCAT (codeur absolu) et
// alimenté tant que l'objet n'est ni en erreur ni désactivé ; une fenêtre dont les deux bornes valent 0 n'est
// pas contrôlée.
FUNCTION_BLOCK FB_Press EXTENDS FB_ObjectBase
VAR
    _pAxis : POINTER TO AXIS_REF;
    _fbPower : MC_Power;
    _fbReset : MC_Reset;
    _fbMoveAbs : MC_MoveAbsolute;
    _fbMoveVel : MC_MoveVelocity;
    _fbHalt : MC_Halt;
    _bEnable : BOOL;
    _bPowered : BOOL;
    _bAbs : BOOL;                   // commande de MC_MoveAbsolute
    _bVel : BOOL;                   // commande de MC_MoveVelocity (segment à force cible)
    _bHalt : BOOL;
    _fTarget : LREAL;
    _fVelo : LREAL;
    _fForce : LREAL;                // N, après tare
    _fForceRaw : LREAL;
    _fStroke : LREAL;               // mm
    _fForceMax : LREAL;
    _nRecipe : INT;                 // rang dans aPressRecipe
    _nRecipeNo : INT;
    _nSeg : USINT;
    _nPhase : USINT;                // 0 lancer le segment, 1 en mouvement, 2 arrêt, 3 retour repos
    _bAllOk : BOOL;
    _nCurveSlot : USINT;
    _nCurveId : UDINT;
    _fOverride : LREAL := 100.0;
    // Simulation
    _fSimStroke : LREAL;
    _fSimVel : LREAL;
    _fSimContact : LREAL;
    _fSimStiffness : LREAL := 200.0; // N/mm
    _bSimBad : BOOL;
END_VAR
VAR PERSISTENT
    _fTare : LREAL;
END_VAR

METHOD PROTECTED _OnConfigure : BOOL
IF _bNewObject THEN _fTare := 0; END_IF
_pAxis := F_AxisRef(_refCfg.nDevice);
_OnConfigure := F_SignalValid(_refCfg.aSignal[1], E_SignalKind.AI) AND (_pAxis <> 0 OR _IsSimulated());
END_METHOD

METHOD PROTECTED _IsMotion : BOOL
VAR_INPUT
    nAction : UINT;
END_VAR
_IsMotion := nAction = GVL_Catalog.Press_ExecuterRecette OR nAction = GVL_Catalog.Press_RetourRepos;
END_METHOD

METHOD PROTECTED _DefaultTimeoutMs : UDINT
VAR_INPUT
    nAction : UINT;
END_VAR
CASE nAction OF
    GVL_Catalog.Press_ExecuterRecette: _DefaultTimeoutMs := LREAL_TO_UDINT(MAX(1000.0, _refCfg.aParam[2]));
    GVL_Catalog.Press_RetourRepos: _DefaultTimeoutMs := 5000;
    GVL_Catalog.Press_TarerLaForce: _DefaultTimeoutMs := 2000;
ELSE
    _DefaultTimeoutMs := 0;
END_CASE
END_METHOD

METHOD PROTECTED _OnStart : E_ActStatus
VAR_INPUT
    nAction : UINT;
END_VAR
VAR
    k : INT;
END_VAR
CASE nAction OF
    GVL_Catalog.Press_ExecuterRecette:
        _nRecipeNo := LREAL_TO_INT(_ParamReal(1, 0));
        _nRecipe := 0;
        FOR k := 1 TO GVL_Const.MAX_PRESS_RECIPES DO
            IF GVL_Active.aPressRecipe[k].bUsed AND GVL_Active.aPressRecipe[k].nStation = _stInfo.nStation
               AND GVL_Active.aPressRecipe[k].nObject = _stInfo.nLocalId AND GVL_Active.aPressRecipe[k].nRecipeNo = _nRecipeNo THEN
                _nRecipe := k;
            END_IF
        END_FOR
        IF _nRecipe = 0 OR_ELSE GVL_Active.aPressRecipe[_nRecipe].nSegments = 0 THEN
            _Fail(GVL_Catalog.Press_Err104, INT_TO_UDINT(_nRecipeNo));
            _OnStart := E_ActStatus.Error;
            RETURN;
        END_IF
        _nSeg := 1;
        _nPhase := 0;
        _bAllOk := TRUE;
        _fForceMax := 0;
        _bSimBad := F_SimRandom() >= 0.97;          // simulation : jugement bon à 97 % (docs/03 § 7.1)
        _fSimContact := _SimContact();
        _BeginCurve();
        _OnStart := E_ActStatus.Busy;
    GVL_Catalog.Press_RetourRepos:
        _fTarget := 0;
        _fVelo := MAX(1.0, _refCfg.aParam[4] * LIMIT(1.0, _refCfg.aParam[3], 100.0) / 100.0);
        _bAbs := FALSE;
        _nPhase := 3;
        _OnStart := E_ActStatus.Busy;
    GVL_Catalog.Press_TarerLaForce:
        _fTare := _fForceRaw;
        _OnStart := E_ActStatus.Done;
    GVL_Catalog.Press_LireForceEtCourse:
        _SetResult(1, _fForce);
        _SetResult(2, _fStroke);
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
    s : REFERENCE TO ST_PressSegment;
    bReached : BOOL;
END_VAR
_OnCycle := E_ActStatus.Busy;
// Force maximale de sécurité : arrêt immédiat, défaut (erreur 100)
IF _fForce > MAX(1.0, _refCfg.aParam[1]) THEN
    _StopMotion();
    _Fail(GVL_Catalog.Press_Err100, LREAL_TO_UDINT(_fForce));
    _OnCycle := E_ActStatus.Error;
    RETURN;
END_IF
IF _nPhase = 3 THEN
    // Retour repos
    IF NOT _bAbs THEN _bAbs := TRUE; RETURN; END_IF
    IF _MoveDone() THEN _bAbs := FALSE; _nPhase := 0; _OnCycle := E_ActStatus.Done; END_IF
    IF _MoveError() THEN _bAbs := FALSE; _Fail(GVL_Catalog.Press_Err101, 0); _OnCycle := E_ActStatus.Error; END_IF
    RETURN;
END_IF
_RecordPoint();
_fForceMax := MAX(_fForceMax, _fForce);
s REF= GVL_Active.aPressRecipe[_nRecipe].aSeg[_nSeg];
CASE _nPhase OF
    0:  // lancer le segment : course cible en position, force cible en vitesse jusqu'à la force
        _fVelo := MAX(0.1, _refCfg.aParam[4] * LIMIT(1.0, s.fSpeedPct, 100.0) / 100.0);
        IF s.eTarget = 0 THEN
            _fTarget := s.fTarget;
            _bAbs := TRUE;
        ELSE
            _bVel := TRUE;
        END_IF
        _nPhase := 1;
    1:
        IF s.eTarget = 0 THEN
            bReached := _MoveDone();
        ELSE
            bReached := _fForce >= s.fTarget;
        END_IF
        IF _MoveError() THEN
            _StopMotion();
            _Fail(GVL_Catalog.Press_Err101, _nSeg);
            _OnCycle := E_ActStatus.Error;
            RETURN;
        END_IF
        IF bReached THEN
            _bAbs := FALSE;
            IF _bVel THEN _bVel := FALSE; _bHalt := TRUE; _nPhase := 2; ELSE _EndSegment(); END_IF
        ELSIF s.eTarget = 1 AND _fStroke >= _MaxStroke(s) THEN
            _StopMotion();                                  // force non atteinte avant la course maximale
            _Fail(GVL_Catalog.Press_Err103, _nSeg);
            _OnCycle := E_ActStatus.Error;
            RETURN;
        END_IF
    2:
        IF _HaltDone() THEN
            _bHalt := FALSE;
            _EndSegment();
        END_IF
END_CASE
IF _nSeg > GVL_Active.aPressRecipe[_nRecipe].nSegments THEN
    _EndCurve();
    _SetJudgement(1, SEL(_bAllOk, E_Judgement.Nok, E_Judgement.Ok));
    _SetResult(2, _fForce);
    _SetResult(3, _fStroke);
    _SetResult(4, _fForceMax);
    _OnCycle := E_ActStatus.Done;
END_IF
END_METHOD

// Fin d'un segment : contrôle des fenêtres de force et de course
METHOD PRIVATE _EndSegment
VAR
    s : REFERENCE TO ST_PressSegment;
END_VAR
s REF= GVL_Active.aPressRecipe[_nRecipe].aSeg[_nSeg];
IF NOT (s.fForceMin = 0 AND s.fForceMax = 0) AND (_fForce < s.fForceMin OR _fForce > s.fForceMax) THEN _bAllOk := FALSE; END_IF
IF NOT (s.fStrokeMin = 0 AND s.fStrokeMax = 0) AND (_fStroke < s.fStrokeMin OR _fStroke > s.fStrokeMax) THEN _bAllOk := FALSE; END_IF
_nSeg := _nSeg + 1;
_nPhase := 0;
END_METHOD

// Course maximale d'un segment à force cible : borne haute de sa fenêtre de course, sinon 200 mm
METHOD PRIVATE _MaxStroke : LREAL
VAR_INPUT
    s : REFERENCE TO ST_PressSegment;
END_VAR
IF s.fStrokeMax > 0 THEN _MaxStroke := s.fStrokeMax; ELSE _MaxStroke := 200.0; END_IF
END_METHOD

METHOD PROTECTED _OnAbort
VAR_INPUT
    nAction : UINT;
END_VAR
_StopMotion();
_EndCurve();
END_METHOD

METHOD PROTECTED _SafeState
_StopMotion();
END_METHOD

METHOD PRIVATE _StopMotion
_bAbs := FALSE;
_bVel := FALSE;
_bHalt := _bPowered AND NOT _IsSimulated();
_fSimVel := 0;
END_METHOD

METHOD PRIVATE _MoveDone : BOOL
IF _IsSimulated() THEN
    _MoveDone := ABS(_fSimStroke - _fTarget) < 0.001;
ELSE
    _MoveDone := _fbMoveAbs.Done;
END_IF
END_METHOD

METHOD PRIVATE _MoveError : BOOL
_MoveError := NOT _IsSimulated() AND (_fbMoveAbs.Error OR _fbMoveVel.Error OR _fbPower.Error);
END_METHOD

METHOD PRIVATE _HaltDone : BOOL
_HaltDone := _IsSimulated() OR _fbHalt.Done OR _fbHalt.Error;
END_METHOD

// ---------- courbe force/course (docs/05 § 9)

METHOD PRIVATE _BeginCurve
_nCurveSlot := GVL_Curves.nNextSlot;
GVL_Curves.nNextSlot := GVL_Curves.nNextSlot MOD GVL_Const.CURVE_BUFFER + 1;
_nCurveId := GVL_Curves.nNextCurveId;
GVL_Curves.nNextCurveId := GVL_Curves.nNextCurveId + 1;
GVL_Hmi.aCurve[_nCurveSlot].nCurveId := 0;          // invalide tant qu'elle s'écrit
GVL_Hmi.aCurve[_nCurveSlot].nPoints := 0;
IF _stInfo.nStation <= GVL_Const.MAX_STATIONS THEN
    GVL_Curves.aLastCurveId[_stInfo.nStation] := _nCurveId;
    GVL_Curves.aLastCurveTime[_stInfo.nStation] := F_NowUtc();
END_IF
END_METHOD

METHOD PRIVATE _RecordPoint
VAR
    c : REFERENCE TO ST_PressCurve;
END_VAR
IF _nCurveSlot = 0 THEN RETURN; END_IF
c REF= GVL_Hmi.aCurve[_nCurveSlot];
IF c.nPoints < GVL_Const.CURVE_POINTS THEN
    c.nPoints := c.nPoints + 1;
    c.aStroke[c.nPoints] := LREAL_TO_REAL(_fStroke);
    c.aForce[c.nPoints] := LREAL_TO_REAL(_fForce);
END_IF
END_METHOD

METHOD PRIVATE _EndCurve
IF _nCurveSlot = 0 THEN RETURN; END_IF
GVL_Hmi.aCurve[_nCurveSlot].nCurveId := _nCurveId;
_nCurveSlot := 0;
END_METHOD

// ---------- matériel et simulation

METHOD PROTECTED _ApplyOverride
VAR_INPUT
    fPercent : LREAL;
END_VAR
_fOverride := fPercent;
END_METHOD

METHOD PROTECTED _ReadHw
_fForceRaw := INT_TO_LREAL(F_ReadAI(_refCfg.aSignal[1]));
_fForce := _fForceRaw - _fTare;
IF _pAxis <> 0 THEN _fStroke := _pAxis^.NcToPlc.ActPos; END_IF
END_METHOD

METHOD PROTECTED _WriteHw
IF _IsSimulated() OR _pAxis = 0 THEN RETURN; END_IF
_bEnable := _eState <> E_ObjState.Error AND _eState <> E_ObjState.Disabled;
_fbPower(Axis := _pAxis^, Enable := _bEnable, Enable_Positive := _bEnable, Enable_Negative := _bEnable, Override := _fOverride);
_bPowered := _fbPower.Status;
_fbMoveAbs(Axis := _pAxis^, Execute := _bAbs, Position := _fTarget, Velocity := _fVelo);
_fbMoveVel(Axis := _pAxis^, Execute := _bVel, Velocity := _fVelo, Direction := MC_Positive_Direction);
_fbHalt(Axis := _pAxis^, Execute := _bHalt);
_fbReset(Axis := _pAxis^, Execute := _eState = E_ObjState.Error AND _fbPower.Error);
END_METHOD

// Simulation : course intégrée à la vitesse commandée, force nulle avant le contact puis raideur constante
METHOD PROTECTED _Simulate
VAR
    fStep : LREAL;
END_VAR
_bPowered := TRUE;
fStep := _fVelo * F_CycleMs() / 1000.0 * _fOverride / 100.0;
IF _bAbs THEN
    IF _fSimStroke < _fTarget THEN _fSimStroke := MIN(_fTarget, _fSimStroke + fStep); ELSE _fSimStroke := MAX(_fTarget, _fSimStroke - fStep); END_IF
ELSIF _bVel THEN
    _fSimStroke := _fSimStroke + fStep;
END_IF
_fStroke := _fSimStroke;
_fForceRaw := MAX(0.0, (_fSimStroke - _fSimContact) * _fSimStiffness) + _fTare;
IF _Forced(1) THEN _fForceRaw := _ForcedValue(1) + _fTare; END_IF
_fForce := _fForceRaw - _fTare;
END_METHOD

// Course du contact simulé : au milieu de la fenêtre de force du dernier segment, décalée si le tirage est mauvais
METHOD PRIVATE _SimContact : LREAL
VAR
    s : REFERENCE TO ST_PressSegment;
    fWanted : LREAL;
END_VAR
s REF= GVL_Active.aPressRecipe[_nRecipe].aSeg[GVL_Active.aPressRecipe[_nRecipe].nSegments];
IF s.eTarget = 0 THEN
    IF s.fForceMax > 0 THEN fWanted := (s.fForceMin + s.fForceMax) / 2.0; ELSE fWanted := 500.0; END_IF
    _SimContact := s.fTarget - fWanted / _fSimStiffness;
    IF _bSimBad THEN _SimContact := _SimContact - 2.0 * MAX(1.0, (s.fForceMax - s.fForceMin)) / _fSimStiffness; END_IF
ELSE
    _SimContact := MAX(1.0, s.fStrokeMin + (s.fStrokeMax - s.fStrokeMin) / 2.0 - s.fTarget / _fSimStiffness);
    IF _bSimBad THEN _SimContact := _SimContact + MAX(1.0, s.fStrokeMax - s.fStrokeMin); END_IF
END_IF
END_METHOD

METHOD PROTECTED _Publish
_stStatus.aValue[1] := _fForce;
_stStatus.aValue[2] := _fStroke;
_stStatus.aValue[3] := INT_TO_LREAL(_nRecipeNo);
END_METHOD
END_FUNCTION_BLOCK
