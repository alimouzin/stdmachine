// Temporisation, type 52 (docs/03 § 8.3)
FUNCTION_BLOCK FB_Timer EXTENDS FB_ObjectBase
VAR
    _fDurationMs : LREAL;
    _fElapsed : LREAL;
END_VAR

METHOD PROTECTED _OnStart : E_ActStatus
VAR_INPUT
    nAction : UINT;
END_VAR
IF nAction = GVL_Catalog.Timer_Attendre THEN
    _fDurationMs := MAX(0.0, _ParamReal(1, _refCfg.aParam[1]));
    _fElapsed := 0;
    _OnStart := _OnCycle(nAction);
ELSE
    _OnStart := E_ActStatus.Error;
END_IF
END_METHOD

METHOD PROTECTED _OnCycle : E_ActStatus
VAR_INPUT
    nAction : UINT;
END_VAR
_fElapsed := _fElapsed + F_CycleMs();
IF _fElapsed >= _fDurationMs THEN _OnCycle := E_ActStatus.Done; ELSE _OnCycle := E_ActStatus.Busy; END_IF
END_METHOD

METHOD PROTECTED _DefaultTimeoutMs : UDINT
VAR_INPUT
    nAction : UINT;
END_VAR
_DefaultTimeoutMs := 0;             // aucun timeout (docs/03 § 8.3)
END_METHOD

METHOD PROTECTED _Publish
IF _eState = E_ObjState.Busy AND _fDurationMs > 0 THEN
    _stStatus.fProgress := LREAL_TO_REAL(MIN(1.0, _fElapsed / _fDurationMs));
END_IF
END_METHOD
END_FUNCTION_BLOCK
