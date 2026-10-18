// Éclairage, type 35 (docs/03 § 6.6) : S1 DO commande, S2 AO intensité (optionnel, 0 à 100 % = 0 à 32767),
// S3 DO synchronisation caméra (optionnel, active pendant un flash). État sûr : éteint.
FUNCTION_BLOCK FB_Light EXTENDS FB_ObjectBase
VAR
    _bOn : BOOL;
    _bSync : BOOL;
    _fIntensity : LREAL;            // %
    _fFlashMs : LREAL;
    _fElapsed : LREAL;
END_VAR

METHOD PROTECTED _OnConfigure : BOOL
_fIntensity := LIMIT(0.0, _refCfg.aParam[2], 100.0);
_OnConfigure := F_SignalValid(_refCfg.aSignal[1], E_SignalKind.DO);
END_METHOD

METHOD PROTECTED _DefaultTimeoutMs : UDINT
VAR_INPUT
    nAction : UINT;
END_VAR
IF nAction = GVL_Catalog.Light_Flasher THEN
    _DefaultTimeoutMs := LREAL_TO_UDINT(MAX(0.0, _ParamReal(1, _refCfg.aParam[3]))) + 200;
ELSE
    _DefaultTimeoutMs := 0;
END_IF
END_METHOD

METHOD PROTECTED _OnStart : E_ActStatus
VAR_INPUT
    nAction : UINT;
END_VAR
CASE nAction OF
    GVL_Catalog.Light_Allumer:
        _fIntensity := LIMIT(0.0, _ParamReal(1, _refCfg.aParam[2]), 100.0);
        _bOn := TRUE;
        _OnStart := E_ActStatus.Done;
    GVL_Catalog.Light_Eteindre:
        _bOn := FALSE;
        _bSync := FALSE;
        _OnStart := E_ActStatus.Done;
    GVL_Catalog.Light_ReglerIntensite:
        _fIntensity := LIMIT(0.0, _ParamReal(1, _fIntensity), 100.0);
        _OnStart := E_ActStatus.Done;
    GVL_Catalog.Light_Flasher:
        _fFlashMs := MAX(0.0, _ParamReal(1, _refCfg.aParam[3]));
        _fIntensity := LIMIT(0.0, _ParamReal(2, _refCfg.aParam[2]), 100.0);
        _fElapsed := 0;
        _bOn := TRUE;
        _bSync := TRUE;
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
IF _fElapsed >= _fFlashMs THEN
    _bOn := FALSE;
    _bSync := FALSE;
    _OnCycle := E_ActStatus.Done;
ELSE
    _OnCycle := E_ActStatus.Busy;
END_IF
END_METHOD

METHOD PROTECTED _OnAbort
VAR_INPUT
    nAction : UINT;
END_VAR
_bOn := FALSE;
_bSync := FALSE;
END_METHOD

METHOD PROTECTED _SafeState
_bOn := FALSE;
_bSync := FALSE;
END_METHOD

METHOD PROTECTED _WriteHw
F_WriteDO(_refCfg.aSignal[1], _bOn);
F_WriteAO(_refCfg.aSignal[2], LREAL_TO_INT(_fIntensity * 32767.0 / 100.0));
F_WriteDO(_refCfg.aSignal[3], _bSync);
END_METHOD

METHOD PROTECTED _Publish
_stStatus.aValue[1] := SEL(_bOn, 0.0, 1.0);
_stStatus.aValue[2] := _fIntensity;
END_METHOD
END_FUNCTION_BLOCK
