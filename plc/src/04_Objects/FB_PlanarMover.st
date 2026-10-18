// Mover, type 14, niveau machine (docs/03 § 4.4) : mode manuel des movers (docs/05 § 6.5). L'élément matériel
// D est le n° du mover ; les mouvements passent par le gestionnaire de movers (GVL_IoRef.ipMovers), qui tient
// l'occupation des positions planaires à jour.
FUNCTION_BLOCK FB_PlanarMover EXTENDS FB_ObjectBase
VAR
    _nMover : USINT;
    _stTarget : ST_Position;
    _stLast : ST_Position;
    _fSpeed : LREAL;                // mm/s, estimée d'un cycle à l'autre
END_VAR

METHOD PROTECTED _OnConfigure : BOOL
_nMover := INT_TO_USINT(LIMIT(0, _refCfg.nDevice, GVL_Const.MAX_MOVERS));
_OnConfigure := _nMover >= 1;
END_METHOD

METHOD PROTECTED _OnStart : E_ActStatus
VAR_INPUT
    nAction : UINT;
END_VAR
VAR
    fPct : LREAL := 100.0;
END_VAR
IF GVL_IoRef.ipMovers = 0 THEN
    _Fail(GVL_Catalog.Err_NotWired, 0);
    _OnStart := E_ActStatus.Error;
    RETURN;
END_IF
CASE nAction OF
    GVL_Catalog.PlanarMover_MoveVersPosition:
        IF _stParams.aKind[1] <> E_ParamKind.Position THEN
            _Fail(GVL_Catalog.Err_BadParam, 1);
            _OnStart := E_ActStatus.Error;
            RETURN;
        END_IF
        _stTarget := _stParams.stPos;
        IF _stParams.aKind[2] = E_ParamKind.SpeedPercent OR _stParams.aKind[2] = E_ParamKind.Real THEN fPct := _stParams.aReal[2]; END_IF
        IF NOT GVL_IoRef.ipMovers.MoveFree(_nMover, _stTarget, fPct * _fEffOverride / 100.0) THEN
            _Fail(GVL_Catalog.Err_NotReady, 0);         // position tenue par un autre mover, ou système inactif
            _OnStart := E_ActStatus.Error;
            RETURN;
        END_IF
        _OnStart := E_ActStatus.Busy;
    GVL_Catalog.PlanarMover_Stop:
        GVL_IoRef.ipMovers.StopMover(_nMover);
        _OnStart := E_ActStatus.Done;
    GVL_Catalog.PlanarMover_LirePosition:
        _SetResultPos(1, GVL_IoRef.ipMovers.Position(_nMover));
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
    stPos : ST_Position;
END_VAR
_OnCycle := E_ActStatus.Busy;
IF nAction <> GVL_Catalog.PlanarMover_MoveVersPosition THEN _OnCycle := E_ActStatus.Done; RETURN; END_IF
stPos := GVL_IoRef.ipMovers.Position(_nMover);
IF NOT GVL_IoRef.ipMovers.Moving(_nMover) THEN
    IF ABS(stPos.fX - _stTarget.fX) <= 0.5 AND ABS(stPos.fY - _stTarget.fY) <= 0.5 THEN
        _OnCycle := E_ActStatus.Done;
    ELSE
        _Fail(GVL_Catalog.Err_Aborted, 0);              // arrêté avant la cible (collision, arrêt du système)
        _OnCycle := E_ActStatus.Error;
    END_IF
END_IF
END_METHOD

METHOD PROTECTED _OnAbort
VAR_INPUT
    nAction : UINT;
END_VAR
IF GVL_IoRef.ipMovers <> 0 THEN GVL_IoRef.ipMovers.StopMover(_nMover); END_IF
END_METHOD

METHOD PROTECTED _Publish
VAR
    stPos : ST_Position;
    fDt : LREAL;
END_VAR
IF GVL_IoRef.ipMovers = 0 THEN RETURN; END_IF
stPos := GVL_IoRef.ipMovers.Position(_nMover);
fDt := F_CycleMs();
IF fDt > 0 THEN _fSpeed := SQRT(EXPT(stPos.fX - _stLast.fX, 2) + EXPT(stPos.fY - _stLast.fY, 2)) * 1000.0 / fDt; END_IF
_stLast := stPos;
_stStatus.aValue[1] := stPos.fX;
_stStatus.aValue[2] := stPos.fY;
_stStatus.aValue[3] := stPos.fC;
_stStatus.aValue[4] := _fSpeed;
END_METHOD
END_FUNCTION_BLOCK
