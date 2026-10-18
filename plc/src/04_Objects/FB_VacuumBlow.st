// Aspiration et soufflage, type 20 (docs/03 § 5.1).
// Capteur de vide : S3 pressostat (DI) ou S4 analogique. Hypothèse (à confirmer) : l'entrée analogique donne le vide
// en dixièmes de kPa (valeur brute ÷ 10).
FUNCTION_BLOCK FB_VacuumBlow EXTENDS FB_ObjectBase
VAR
    _bSuck : BOOL;
    _bBlow : BOOL;
    _bHasBlow : BOOL;
    _bDigital : BOOL;               // pressostat S3
    _bAnalog : BOOL;                // capteur S4
    _fVacuum : LREAL;               // kPa (négatif), ou 0/1 pour le pressostat
    _bPresent : BOOL;
    _bEmptyNozzle : BOOL;           // vide sous le seuil « buse vide »
    _bHolding : BOOL;               // pièce prise : une perte de vide est une erreur 101
    _bWaitVacuum : BOOL;
    _fBlowMs : LREAL;
    _fElapsed : LREAL;
    _fSimVacuum : LREAL;
END_VAR

METHOD PROTECTED _OnConfigure : BOOL
_bHasBlow := F_SignalValid(_refCfg.aSignal[2], E_SignalKind.DO);
_bDigital := F_SignalValid(_refCfg.aSignal[3], E_SignalKind.DI);
_bAnalog := F_SignalValid(_refCfg.aSignal[4], E_SignalKind.AI);
_bHolding := FALSE;
_OnConfigure := F_SignalValid(_refCfg.aSignal[1], E_SignalKind.DO) AND (_bDigital OR _bAnalog);
END_METHOD

METHOD PROTECTED _DefaultTimeoutMs : UDINT
VAR_INPUT
    nAction : UINT;
END_VAR
IF nAction = GVL_Catalog.VacuumBlow_Souffler THEN
    _DefaultTimeoutMs := LREAL_TO_UDINT(_ParamReal(1, _refCfg.aParam[3])) + 500;
ELSE
    _DefaultTimeoutMs := SUPER^._DefaultTimeoutMs(nAction);
END_IF
END_METHOD

METHOD PROTECTED _OnStart : E_ActStatus
VAR_INPUT
    nAction : UINT;
END_VAR
_fElapsed := 0;
CASE nAction OF
    GVL_Catalog.VacuumBlow_Aspirer:
        _bBlow := FALSE;
        _bSuck := TRUE;
        _bWaitVacuum := _ParamReal(1, 1.0) >= 0.5;
        IF NOT _bWaitVacuum THEN
            _SetResult(1, BOOL_TO_LREAL(_bPresent));
            _OnStart := E_ActStatus.Done;
        ELSE
            _OnStart := E_ActStatus.Busy;
        END_IF
    GVL_Catalog.VacuumBlow_Souffler:
        _bSuck := FALSE;
        _bHolding := FALSE;
        _fBlowMs := MAX(_ParamReal(1, _refCfg.aParam[3]), 0.0);
        _bBlow := _bHasBlow;
        _OnStart := E_ActStatus.Busy;
    GVL_Catalog.VacuumBlow_Couper:
        _bSuck := FALSE;
        _bBlow := FALSE;
        _bHolding := FALSE;
        _OnStart := E_ActStatus.Done;
    GVL_Catalog.VacuumBlow_VerifierPresencePiece:
        _SetResult(1, BOOL_TO_LREAL(_bPresent));
        _OnStart := E_ActStatus.Done;
ELSE
    _OnStart := E_ActStatus.Error;
END_CASE
END_METHOD

METHOD PROTECTED _OnCycle : E_ActStatus
VAR_INPUT
    nAction : UINT;
END_VAR
_fElapsed := _fElapsed + F_CycleMs();
CASE nAction OF
    GVL_Catalog.VacuumBlow_Aspirer:
        IF _bPresent THEN
            _bHolding := TRUE;
            _SetResult(1, 1.0);
            _OnCycle := E_ActStatus.Done;
        ELSIF _nTimeoutMs > 0 AND _fElapsed + F_CycleMs() >= UDINT_TO_LREAL(_nTimeoutMs) THEN
            _Fail(GVL_Catalog.VacuumBlow_Err100, 0);        // vide non atteint, avant le timeout générique
            _OnCycle := E_ActStatus.Error;
        ELSE
            _OnCycle := E_ActStatus.Busy;
        END_IF
    GVL_Catalog.VacuumBlow_Souffler:
        IF _fElapsed >= _fBlowMs THEN
            _bBlow := FALSE;
            _OnCycle := E_ActStatus.Done;
        ELSE
            _OnCycle := E_ActStatus.Busy;
        END_IF
ELSE
    _OnCycle := E_ActStatus.Done;
END_CASE
END_METHOD

METHOD PROTECTED _OnAbort
VAR_INPUT
    nAction : UINT;
END_VAR
_bBlow := FALSE;
END_METHOD

METHOD PROTECTED _SafeState
_bBlow := FALSE;
IF _refCfg.aParam[4] = 0 THEN
    _bSuck := FALSE;                // tout couper
    _bHolding := FALSE;
END_IF                              // 1 : maintenir l'aspiration
END_METHOD

METHOD PROTECTED _Monitor
IF _bAnalog AND _refCfg.aParam[1] >= _refCfg.aParam[2] THEN
    IF _eState <> E_ObjState.Error THEN _Fail(GVL_Catalog.VacuumBlow_Err102, 0); END_IF
    RETURN;
END_IF
IF _bHolding AND _bSuck AND NOT _bPresent AND _eState <> E_ObjState.Error THEN
    _bHolding := FALSE;
    _Fail(GVL_Catalog.VacuumBlow_Err101, 0);       // pièce tombée
END_IF
END_METHOD

METHOD PROTECTED _ReadHw
IF _bDigital THEN
    _bPresent := F_ReadDI(_refCfg.aSignal[3]);
    _fVacuum := BOOL_TO_LREAL(_bPresent);
ELSE
    _fVacuum := INT_TO_LREAL(F_ReadAI(_refCfg.aSignal[4])) / 10.0;
    _Evaluate();
END_IF
END_METHOD

METHOD PROTECTED _Simulate
VAR
    fStep : LREAL;
END_VAR
// Vide simulé : −70 kPa en 80 ms quand on aspire, retour à 0 sinon
fStep := 70.0 * F_CycleMs() / 80.0;
IF _bSuck THEN _fSimVacuum := MAX(-70.0, _fSimVacuum - fStep); ELSE _fSimVacuum := MIN(0.0, _fSimVacuum + fStep); END_IF
IF _Forced(4) THEN _fSimVacuum := _ForcedValue(4); END_IF
IF _bDigital THEN
    _bPresent := _fSimVacuum <= -60.0;
    IF _Forced(3) THEN _bPresent := _ForcedValue(3) >= 0.5; END_IF
    _fVacuum := BOOL_TO_LREAL(_bPresent);
ELSE
    _fVacuum := _fSimVacuum;
    _Evaluate();
END_IF
END_METHOD

METHOD PRIVATE _Evaluate
// Hystérésis entre P1 (pièce présente, −60 kPa) et P2 (buse vide, −52 kPa)
IF _fVacuum <= _refCfg.aParam[1] THEN
    _bPresent := TRUE;
ELSIF _fVacuum >= _refCfg.aParam[2] THEN
    _bPresent := FALSE;
END_IF
_bEmptyNozzle := _fVacuum >= _refCfg.aParam[2];
END_METHOD

METHOD PROTECTED _WriteHw
F_WriteDO(_refCfg.aSignal[1], _bSuck AND NOT _bBlow);     // jamais les deux ensemble
IF _bHasBlow THEN F_WriteDO(_refCfg.aSignal[2], _bBlow AND NOT _bSuck); END_IF
END_METHOD

METHOD PROTECTED _Publish
_stStatus.aValue[1] := BOOL_TO_LREAL(_bSuck);
_stStatus.aValue[2] := BOOL_TO_LREAL(_bBlow);
_stStatus.aValue[3] := _fVacuum;
_stStatus.aValue[4] := BOOL_TO_LREAL(_bPresent);
END_METHOD
END_FUNCTION_BLOCK
