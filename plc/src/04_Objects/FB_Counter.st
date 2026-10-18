// Compteur, type 53 (docs/03 § 8.4)
FUNCTION_BLOCK FB_Counter EXTENDS FB_ObjectBase IMPLEMENTS I_OrderAware
VAR
    _bStarted : BOOL;               // faux au démarrage du PLC (P4 = 0 : la valeur repart de P1)
END_VAR
VAR PERSISTENT
    _fValue : LREAL;
END_VAR

METHOD PROTECTED _OnConfigure : BOOL
IF _bNewObject THEN _fValue := _refCfg.aParam[1]; END_IF
_OnConfigure := TRUE;
END_METHOD

METHOD PROTECTED _OnStart : E_ActStatus
VAR_INPUT
    nAction : UINT;
END_VAR
CASE nAction OF
    GVL_Catalog.Counter_Incrementer:
        _fValue := _fValue + _ParamReal(1, 1.0);
        _SetResult(1, _fValue);
        IF _refCfg.aParam[2] > 0 AND _fValue > _refCfg.aParam[2] THEN
            _Fail(GVL_Catalog.Counter_Err100, LREAL_TO_UDINT(_fValue));
            _OnStart := E_ActStatus.Error;
            RETURN;
        END_IF
    GVL_Catalog.Counter_Lire:
        _SetResult(1, _fValue);
    GVL_Catalog.Counter_RemettreAZero:
        _fValue := _refCfg.aParam[1];
    GVL_Catalog.Counter_Comparer:
        _SetResult(1, BOOL_TO_LREAL(_fValue >= _ParamReal(1, 0.0)));
ELSE
    _OnStart := E_ActStatus.Error;
    RETURN;
END_CASE
_OnStart := E_ActStatus.Done;
END_METHOD

// Remise à zéro au changement d'OF (P3), appelée par le séquenceur général
METHOD OnOrderChange
IF _refCfg.aParam[3] = 1 THEN _fValue := _refCfg.aParam[1]; END_IF
END_METHOD

METHOD PROTECTED _Monitor
IF NOT _bStarted THEN
    _bStarted := TRUE;
    IF _refCfg.aParam[4] = 0 THEN _fValue := _refCfg.aParam[1]; END_IF
END_IF
END_METHOD

METHOD PROTECTED _Publish
_stStatus.aValue[1] := _fValue;
END_METHOD
END_FUNCTION_BLOCK
