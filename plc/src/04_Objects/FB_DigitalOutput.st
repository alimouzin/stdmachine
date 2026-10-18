// Sortie numérique, type 2 (docs/03 § 3.2)
FUNCTION_BLOCK FB_DigitalOutput EXTENDS FB_ObjectBase
VAR
    _bCmd : BOOL;
    _fPulseMs : LREAL;
    _fElapsed : LREAL;
END_VAR

METHOD PROTECTED _OnConfigure : BOOL
_OnConfigure := F_SignalValid(_refCfg.aSignal[1], E_SignalKind.DO);
END_METHOD

METHOD PROTECTED _DefaultTimeoutMs : UDINT
VAR_INPUT
    nAction : UINT;
END_VAR
IF nAction = GVL_Catalog.DigitalOutput_Impulsion THEN
    _DefaultTimeoutMs := LREAL_TO_UDINT(_ParamReal(1, _refCfg.aParam[2])) + 1000;
ELSE
    _DefaultTimeoutMs := 0;
END_IF
END_METHOD

METHOD PROTECTED _ValidateParams : BOOL
VAR_INPUT
    nAction : UINT;
END_VAR
_ValidateParams := nAction <> GVL_Catalog.DigitalOutput_Impulsion OR _ParamReal(1, _refCfg.aParam[2]) >= 1.0;
END_METHOD

METHOD PROTECTED _OnStart : E_ActStatus
VAR_INPUT
    nAction : UINT;
END_VAR
CASE nAction OF
    GVL_Catalog.DigitalOutput_Activer:
        _bCmd := TRUE;
        _OnStart := E_ActStatus.Done;
    GVL_Catalog.DigitalOutput_Desactiver:
        _bCmd := FALSE;
        _OnStart := E_ActStatus.Done;
    GVL_Catalog.DigitalOutput_Impulsion:
        _fPulseMs := LIMIT(1.0, _ParamReal(1, _refCfg.aParam[2]), 60000.0);
        _fElapsed := 0;
        _bCmd := TRUE;
        _OnStart := E_ActStatus.Busy;
ELSE
    _OnStart := E_ActStatus.Error;
END_CASE
END_METHOD

METHOD PROTECTED _OnCycle : E_ActStatus
VAR_INPUT
    nAction : UINT;
END_VAR
_fElapsed := _fElapsed + F_CycleMs();
IF _fElapsed >= _fPulseMs THEN
    _bCmd := FALSE;
    _OnCycle := E_ActStatus.Done;
ELSE
    _OnCycle := E_ActStatus.Busy;
END_IF
END_METHOD

METHOD PROTECTED _SafeState
CASE LREAL_TO_INT(_refCfg.aParam[3]) OF
    0: _bCmd := FALSE;
    1: _bCmd := TRUE;
END_CASE                            // 2 : maintien
END_METHOD

METHOD PROTECTED _WriteHw
F_WriteDO(_refCfg.aSignal[1], _bCmd XOR (_refCfg.aParam[1] = 1));
END_METHOD

METHOD PROTECTED _Publish
_stStatus.aValue[1] := BOOL_TO_LREAL(_bCmd);
END_METHOD
END_FUNCTION_BLOCK
