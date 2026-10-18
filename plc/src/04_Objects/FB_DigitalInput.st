// Entrée numérique, type 1 (docs/03 § 3.1)
FUNCTION_BLOCK FB_DigitalInput EXTENDS FB_ObjectBase
VAR
    _bRaw : BOOL;                   // valeur lue (inversion P1 appliquée)
    _bValue : BOOL;                 // valeur filtrée (P2)
    _fDebounceMs : LREAL;
    _fChangeMs : LREAL;             // durée depuis laquelle la valeur lue diffère de la valeur filtrée
    _fWaitMs : LREAL;
END_VAR

METHOD PROTECTED _OnConfigure : BOOL
_fDebounceMs := LIMIT(0.0, _refCfg.aParam[2], 1000.0);
_OnConfigure := F_SignalValid(_refCfg.aSignal[1], E_SignalKind.DI);
END_METHOD

METHOD PROTECTED _OnStart : E_ActStatus
VAR_INPUT
    nAction : UINT;
END_VAR
_fWaitMs := 0;
CASE nAction OF
    GVL_Catalog.DigitalInput_Lire:
        _SetResult(1, BOOL_TO_LREAL(_bValue));
        _OnStart := E_ActStatus.Done;
    GVL_Catalog.DigitalInput_AttendreActif, GVL_Catalog.DigitalInput_AttendreInactif:
        _OnStart := _OnCycle(nAction);
ELSE
    _OnStart := E_ActStatus.Error;
END_CASE
END_METHOD

METHOD PROTECTED _OnCycle : E_ActStatus
VAR_INPUT
    nAction : UINT;
END_VAR
_fWaitMs := _fWaitMs + F_CycleMs();
// En simulation, sans forçage, l'attente se termine après 300 ms (docs/03 § 3.1)
IF _IsSimulated() AND NOT _Forced(1) AND _fWaitMs >= 300.0 THEN
    _bValue := nAction = GVL_Catalog.DigitalInput_AttendreActif;
END_IF
IF (nAction = GVL_Catalog.DigitalInput_AttendreActif) = _bValue THEN
    _OnCycle := E_ActStatus.Done;
ELSE
    _OnCycle := E_ActStatus.Busy;
END_IF
END_METHOD

METHOD PROTECTED _ReadHw
_bRaw := F_ReadDI(_refCfg.aSignal[1]) XOR (_refCfg.aParam[1] = 1);
_Debounce();
END_METHOD

METHOD PROTECTED _Simulate
IF _Forced(1) THEN
    _bRaw := _ForcedValue(1) >= 0.5;
    _Debounce();
END_IF
END_METHOD

METHOD PRIVATE _Debounce
IF _bRaw = _bValue THEN
    _fChangeMs := 0;
ELSE
    _fChangeMs := _fChangeMs + F_CycleMs();
    IF _fChangeMs >= _fDebounceMs THEN
        _bValue := _bRaw;
        _fChangeMs := 0;
    END_IF
END_IF
END_METHOD

METHOD PROTECTED _Publish
_stStatus.aValue[1] := BOOL_TO_LREAL(_bValue);
END_METHOD

PROPERTY Value : BOOL
GET
Value := _bValue;
END_GET
END_PROPERTY
END_FUNCTION_BLOCK
