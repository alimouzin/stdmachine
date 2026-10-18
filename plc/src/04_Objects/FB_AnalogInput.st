// Entrée analogique, type 3 (docs/03 § 3.3)
FUNCTION_BLOCK FB_AnalogInput EXTENDS FB_ObjectBase
VAR
    _aSamples : ARRAY[1..100] OF LREAL;
    _nSamples : INT;
    _nNext : INT := 1;
    _fValue : LREAL;                // valeur filtrée, en unité
    _bLowActive : BOOL;
    _bHighActive : BOOL;
END_VAR

METHOD PROTECTED _OnConfigure : BOOL
_nSamples := 0;
_nNext := 1;
_OnConfigure := F_SignalValid(_refCfg.aSignal[1], E_SignalKind.AI) AND _refCfg.aParam[2] <> _refCfg.aParam[1];
END_METHOD

METHOD PROTECTED _OnStart : E_ActStatus
VAR_INPUT
    nAction : UINT;
END_VAR
CASE nAction OF
    GVL_Catalog.AnalogInput_Lire:
        _SetResult(1, _fValue);
        _OnStart := E_ActStatus.Done;
    GVL_Catalog.AnalogInput_AttendreSeuil:
        _OnStart := _OnCycle(nAction);
    GVL_Catalog.AnalogInput_VerifierPlage:
        _SetResult(1, _fValue);
        IF _fValue < _ParamReal(1, -1.0E38) THEN
            _SetJudgement(2, E_Judgement.Low);
        ELSIF _fValue > _ParamReal(2, 1.0E38) THEN
            _SetJudgement(2, E_Judgement.High);
        ELSE
            _SetJudgement(2, E_Judgement.Ok);
        END_IF
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
    bReached : BOOL;
END_VAR
IF _ParamReal(2, 0.0) = 0.0 THEN
    bReached := _fValue >= _ParamReal(1, 0.0);      // au-dessus
ELSE
    bReached := _fValue <= _ParamReal(1, 0.0);      // en dessous
END_IF
IF bReached THEN
    _SetResult(1, _fValue);
    _OnCycle := E_ActStatus.Done;
ELSE
    _OnCycle := E_ActStatus.Busy;
END_IF
END_METHOD

METHOD PROTECTED _ReadHw
_Filter(_Scale(INT_TO_LREAL(F_ReadAI(_refCfg.aSignal[1]))));
END_METHOD

METHOD PROTECTED _Simulate
IF _Forced(1) THEN _Filter(_ForcedValue(1)); END_IF        // valeur forcée en unité
END_METHOD

METHOD PRIVATE _Scale : LREAL
VAR_INPUT
    fRaw : LREAL;
END_VAR
_Scale := _refCfg.aParam[3] + (fRaw - _refCfg.aParam[1]) * (_refCfg.aParam[4] - _refCfg.aParam[3]) / (_refCfg.aParam[2] - _refCfg.aParam[1]);
END_METHOD

METHOD PRIVATE _Filter
VAR_INPUT
    fUnit : LREAL;
END_VAR
VAR
    nWin : INT;
    i : INT;
    fSum : LREAL;
END_VAR
nWin := LIMIT(1, LREAL_TO_INT(_refCfg.aParam[6]), 100);
_aSamples[_nNext] := fUnit;
_nNext := _nNext MOD nWin + 1;
_nSamples := MIN(_nSamples + 1, nWin);
fSum := 0;
FOR i := 1 TO _nSamples DO fSum := fSum + _aSamples[i]; END_FOR
_fValue := fSum / INT_TO_LREAL(_nSamples);
END_METHOD

// Seuils d'alarme : avertissements surveillés en continu, sans mettre l'objet en erreur
METHOD PROTECTED _Monitor
IF _ipAlarms = 0 THEN RETURN; END_IF
IF _fValue < _refCfg.aParam[7] AND NOT _bLowActive THEN
    _ipAlarms.Raise(_stInfo.nGlobalId, GVL_Catalog.AnalogInput_Err100, 0, E_Severity.Warning);
    _bLowActive := TRUE;
ELSIF _fValue >= _refCfg.aParam[7] AND _bLowActive THEN
    _ipAlarms.Clear(_stInfo.nGlobalId, GVL_Catalog.AnalogInput_Err100);
    _bLowActive := FALSE;
END_IF
IF _fValue > _refCfg.aParam[8] AND NOT _bHighActive THEN
    _ipAlarms.Raise(_stInfo.nGlobalId, GVL_Catalog.AnalogInput_Err101, 0, E_Severity.Warning);
    _bHighActive := TRUE;
ELSIF _fValue <= _refCfg.aParam[8] AND _bHighActive THEN
    _ipAlarms.Clear(_stInfo.nGlobalId, GVL_Catalog.AnalogInput_Err101);
    _bHighActive := FALSE;
END_IF
END_METHOD

METHOD PROTECTED _Publish
_stStatus.aValue[1] := _fValue;
END_METHOD

PROPERTY Value : LREAL
GET
Value := _fValue;
END_GET
END_PROPERTY
END_FUNCTION_BLOCK
