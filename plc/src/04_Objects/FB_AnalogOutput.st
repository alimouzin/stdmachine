// Sortie analogique, type 4 (docs/03 § 3.4)
FUNCTION_BLOCK FB_AnalogOutput EXTENDS FB_ObjectBase
VAR
    _fValue : LREAL;                // valeur commandée, en unité
    _fStart : LREAL;
    _fTarget : LREAL;
    _fRampMs : LREAL;
    _fElapsed : LREAL;
END_VAR

METHOD PROTECTED _OnConfigure : BOOL
_OnConfigure := F_SignalValid(_refCfg.aSignal[1], E_SignalKind.AO) AND _refCfg.aParam[4] <> _refCfg.aParam[3];
END_METHOD

METHOD PROTECTED _DefaultTimeoutMs : UDINT
VAR_INPUT
    nAction : UINT;
END_VAR
IF nAction = GVL_Catalog.AnalogOutput_EcrireAvecRampe THEN
    _DefaultTimeoutMs := LREAL_TO_UDINT(MAX(_ParamReal(2, 0.0), 0.0)) + 1000;
ELSE
    _DefaultTimeoutMs := 0;
END_IF
END_METHOD

METHOD PROTECTED _OnStart : E_ActStatus
VAR_INPUT
    nAction : UINT;
END_VAR
_fStart := _fValue;
_fElapsed := 0;
CASE nAction OF
    GVL_Catalog.AnalogOutput_Ecrire:
        _fTarget := _Limit(_ParamReal(1, _fValue));
        _fRampMs := 0;
    GVL_Catalog.AnalogOutput_EcrireAvecRampe:
        _fTarget := _Limit(_ParamReal(1, _fValue));
        _fRampMs := MAX(_ParamReal(2, 0.0), 0.0);
    GVL_Catalog.AnalogOutput_ValeurSure:
        _fTarget := _Limit(_refCfg.aParam[8]);
        _fRampMs := 0;
ELSE
    _OnStart := E_ActStatus.Error;
    RETURN;
END_CASE
_OnStart := _OnCycle(nAction);
END_METHOD

METHOD PROTECTED _OnCycle : E_ActStatus
VAR_INPUT
    nAction : UINT;
END_VAR
VAR
    fNext : LREAL;
    fMaxStep : LREAL;
END_VAR
_fElapsed := _fElapsed + F_CycleMs();
IF _fRampMs > 0 THEN
    fNext := _fStart + (_fTarget - _fStart) * MIN(1.0, _fElapsed / _fRampMs);
ELSE
    fNext := _fTarget;
END_IF
// Variation maximale P9 (unité/s), 0 = illimitée
IF _refCfg.aParam[9] > 0 THEN
    fMaxStep := _refCfg.aParam[9] * F_CycleMs() / 1000.0;
    fNext := LIMIT(_fValue - fMaxStep, fNext, _fValue + fMaxStep);
END_IF
_fValue := fNext;
IF ABS(_fValue - _fTarget) < 1.0E-9 THEN _OnCycle := E_ActStatus.Done; ELSE _OnCycle := E_ActStatus.Busy; END_IF
END_METHOD

METHOD PRIVATE _Limit : LREAL
VAR_INPUT
    fValue : LREAL;
END_VAR
_Limit := LIMIT(_refCfg.aParam[6], fValue, _refCfg.aParam[7]);
END_METHOD

METHOD PROTECTED _SafeState
_fValue := _Limit(_refCfg.aParam[8]);
END_METHOD

METHOD PROTECTED _WriteHw
VAR
    fRaw : LREAL;
END_VAR
fRaw := _refCfg.aParam[1] + (_fValue - _refCfg.aParam[3]) * (_refCfg.aParam[2] - _refCfg.aParam[1]) / (_refCfg.aParam[4] - _refCfg.aParam[3]);
F_WriteAO(_refCfg.aSignal[1], LREAL_TO_INT(LIMIT(-32768.0, fRaw, 32767.0)));
END_METHOD

METHOD PROTECTED _Publish
_stStatus.aValue[1] := _fValue;
END_METHOD
END_FUNCTION_BLOCK
