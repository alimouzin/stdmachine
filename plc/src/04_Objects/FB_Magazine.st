// Palettiseur ou magasin, type 45 (docs/03 § 7.6) : S1 DI plateau présent, S2 DI magasin vide, S3 DI magasin
// plein, S4 DO présenter, S5 DO évacuer. Cases numérotées ligne par ligne depuis la première case (position
// nommée P5 de la station), pas P3 en X et P4 en Y. L'occupation du plateau présent est persistante.
FUNCTION_BLOCK FB_Magazine EXTENDS FB_ObjectBase
VAR
    _bPresent : BOOL;
    _bEmpty : BOOL;
    _bFull : BOOL;
    _bCmdPresent : BOOL;
    _bCmdEvacuate : BOOL;
    _nRows : INT;
    _nCols : INT;
    _fSimMs : LREAL;
    _bSimPresent : BOOL;
END_VAR
VAR PERSISTENT
    _aOccupied : ARRAY[1..256] OF BOOL;
END_VAR

METHOD PROTECTED _OnConfigure : BOOL
_nRows := LIMIT(1, LREAL_TO_INT(_refCfg.aParam[1]), 16);
_nCols := LIMIT(1, LREAL_TO_INT(_refCfg.aParam[2]), 16);
IF _bNewObject THEN MEMSET(ADR(_aOccupied), 0, SIZEOF(_aOccupied)); END_IF
_OnConfigure := F_SignalValid(_refCfg.aSignal[1], E_SignalKind.DI)
                AND F_SignalValid(_refCfg.aSignal[4], E_SignalKind.DO)
                AND F_SignalValid(_refCfg.aSignal[5], E_SignalKind.DO);
END_METHOD

METHOD PROTECTED _OnStart : E_ActStatus
VAR_INPUT
    nAction : UINT;
END_VAR
VAR
    n : INT;
END_VAR
CASE nAction OF
    GVL_Catalog.Magazine_PresenterPlateauSuivant:
        IF _bEmpty THEN _Fail(GVL_Catalog.Magazine_Err100, 0); _OnStart := E_ActStatus.Error; RETURN; END_IF
        _bCmdPresent := TRUE;
        _fSimMs := 0;
        _OnStart := E_ActStatus.Busy;
    GVL_Catalog.Magazine_EvacuerPlateau:
        IF NOT _bPresent THEN _Fail(GVL_Catalog.Magazine_Err102, 0); _OnStart := E_ActStatus.Error; RETURN; END_IF
        IF _bFull THEN _Fail(GVL_Catalog.Magazine_Err101, 0); _OnStart := E_ActStatus.Error; RETURN; END_IF
        _bCmdEvacuate := TRUE;
        _fSimMs := 0;
        _OnStart := E_ActStatus.Busy;
    GVL_Catalog.Magazine_PositionDeLaCase:
        n := LREAL_TO_INT(_ParamReal(1, 0));
        IF n = 0 THEN n := _FirstFree(); END_IF
        IF n < 1 OR n > _nRows * _nCols THEN _Fail(GVL_Catalog.Err_BadParam, 1); _OnStart := E_ActStatus.Error; RETURN; END_IF
        _SetResultPos(1, _CellPos(n));
        _SetResult(2, INT_TO_LREAL(n));
        _OnStart := E_ActStatus.Done;
    GVL_Catalog.Magazine_MarquerCase:
        n := LREAL_TO_INT(_ParamReal(1, 0));
        IF n < 1 OR n > _nRows * _nCols THEN _Fail(GVL_Catalog.Err_BadParam, 1); _OnStart := E_ActStatus.Error; RETURN; END_IF
        _aOccupied[n] := _ParamReal(2, 1) <> 0;
        _OnStart := E_ActStatus.Done;
    GVL_Catalog.Magazine_LireOccupation:
        n := _CountOccupied();
        _SetResult(1, INT_TO_LREAL(n));
        _SetResult(2, INT_TO_LREAL(_nRows * _nCols - n));
        _SetResult(3, SEL(n >= _nRows * _nCols, 0.0, 1.0));
        _OnStart := E_ActStatus.Done;
ELSE
    _OnStart := E_ActStatus.Error;
END_CASE
END_METHOD

METHOD PROTECTED _OnCycle : E_ActStatus
VAR_INPUT
    nAction : UINT;
END_VAR
_OnCycle := E_ActStatus.Busy;
CASE nAction OF
    GVL_Catalog.Magazine_PresenterPlateauSuivant:
        IF _bPresent THEN
            _bCmdPresent := FALSE;
            MEMSET(ADR(_aOccupied), 0, SIZEOF(_aOccupied));        // plateau neuf : toutes les cases libres
            _OnCycle := E_ActStatus.Done;
        ELSIF _fElapsedMs + 100.0 >= UDINT_TO_LREAL(_nTimeoutMs) AND _nTimeoutMs > 0 THEN
            _bCmdPresent := FALSE;
            _Fail(GVL_Catalog.Magazine_Err102, 0);
            _OnCycle := E_ActStatus.Error;
        END_IF
    GVL_Catalog.Magazine_EvacuerPlateau:
        IF NOT _bPresent THEN
            _bCmdEvacuate := FALSE;
            _OnCycle := E_ActStatus.Done;
        ELSIF _fElapsedMs + 100.0 >= UDINT_TO_LREAL(_nTimeoutMs) AND _nTimeoutMs > 0 THEN
            _bCmdEvacuate := FALSE;
            _Fail(GVL_Catalog.Magazine_Err103, 0);
            _OnCycle := E_ActStatus.Error;
        END_IF
ELSE
    _OnCycle := E_ActStatus.Done;
END_CASE
END_METHOD

METHOD PROTECTED _OnAbort
VAR_INPUT
    nAction : UINT;
END_VAR
_bCmdPresent := FALSE;
_bCmdEvacuate := FALSE;
END_METHOD

METHOD PROTECTED _SafeState
_bCmdPresent := FALSE;
_bCmdEvacuate := FALSE;
END_METHOD

METHOD PROTECTED _ReadHw
_bPresent := F_ReadDI(_refCfg.aSignal[1]);
_bEmpty := F_ReadDI(_refCfg.aSignal[2]);
_bFull := F_ReadDI(_refCfg.aSignal[3]);
END_METHOD

METHOD PROTECTED _WriteHw
F_WriteDO(_refCfg.aSignal[4], _bCmdPresent);
F_WriteDO(_refCfg.aSignal[5], _bCmdEvacuate);
END_METHOD

// Simulation : un plateau arrive ou part 1 s après la commande ; le magasin n'est jamais vide ni plein
METHOD PROTECTED _Simulate
IF _bCmdPresent OR _bCmdEvacuate THEN _fSimMs := _fSimMs + F_CycleMs(); END_IF
IF _bCmdPresent AND _fSimMs >= 1000.0 THEN _bSimPresent := TRUE; END_IF
IF _bCmdEvacuate AND _fSimMs >= 1000.0 THEN _bSimPresent := FALSE; END_IF
_bPresent := _bSimPresent;
_bEmpty := FALSE;
_bFull := FALSE;
IF _Forced(1) THEN _bPresent := _ForcedValue(1) >= 0.5; END_IF
IF _Forced(2) THEN _bEmpty := _ForcedValue(2) >= 0.5; END_IF
IF _Forced(3) THEN _bFull := _ForcedValue(3) >= 0.5; END_IF
END_METHOD

METHOD PRIVATE _FirstFree : INT
VAR
    i : INT;
END_VAR
_FirstFree := 0;
FOR i := 1 TO _nRows * _nCols DO
    IF NOT _aOccupied[i] THEN _FirstFree := i; RETURN; END_IF
END_FOR
END_METHOD

METHOD PRIVATE _CountOccupied : INT
VAR
    i : INT;
END_VAR
_CountOccupied := 0;
FOR i := 1 TO _nRows * _nCols DO
    IF _aOccupied[i] THEN _CountOccupied := _CountOccupied + 1; END_IF
END_FOR
END_METHOD

// Position de la case n : première case (position nommée P5 de la station) + décalage de colonne et de ligne
METHOD PRIVATE _CellPos : ST_Position
VAR_INPUT
    n : INT;
END_VAR
VAR
    nPos : INT;
END_VAR
nPos := LREAL_TO_INT(_refCfg.aParam[5]);
IF _stInfo.nStation >= 1 AND _stInfo.nStation <= GVL_Const.MAX_STATIONS AND nPos >= 1 AND nPos <= GVL_Const.MAX_NAMED_POS THEN
    _CellPos := GVL_Active.aStation[_stInfo.nStation].aPos[nPos].stPos;
END_IF
_CellPos.fX := _CellPos.fX + INT_TO_LREAL((n - 1) MOD _nCols) * _refCfg.aParam[3];
_CellPos.fY := _CellPos.fY + INT_TO_LREAL((n - 1) / _nCols) * _refCfg.aParam[4];
END_METHOD

METHOD PROTECTED _Publish
_stStatus.aValue[1] := SEL(_bPresent, 0.0, 1.0);
_stStatus.aValue[2] := INT_TO_LREAL(_CountOccupied());
END_METHOD
END_FUNCTION_BLOCK
