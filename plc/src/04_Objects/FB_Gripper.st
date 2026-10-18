// Pince, type 21 (docs/03 § 5.2). Seul le pneumatique est réalisé ; l'électrique (P1 = 2) attend son interface :
// la configuration est alors refusée.
FUNCTION_BLOCK FB_Gripper EXTENDS FB_ObjectBase
VAR
    _bBistable : BOOL;
    _bCmdClose : BOOL;
    _bClosed : BOOL;
    _bOpen : BOOL;
    _bPart : BOOL;
    _bHasClosed : BOOL;
    _bHasOpen : BOOL;
    _bHasPart : BOOL;
    _bHolding : BOOL;               // fermée sur pièce : une perte de pièce est une erreur 101
    _fMove : LREAL;
    _fSimPos : LREAL;               // 0 ouverte, 1 fermée
    _bSimPart : BOOL := TRUE;       // simulation : une pièce est présente entre les doigts
END_VAR

METHOD PROTECTED _OnConfigure : BOOL
_bBistable := _refCfg.aParam[1] = 1;
_bHasClosed := F_SignalValid(_refCfg.aSignal[3], E_SignalKind.DI);
_bHasOpen := F_SignalValid(_refCfg.aSignal[4], E_SignalKind.DI);
_bHasPart := F_SignalValid(_refCfg.aSignal[5], E_SignalKind.DI);
_bHolding := FALSE;
_OnConfigure := _refCfg.aParam[1] < 2
                AND F_SignalValid(_refCfg.aSignal[1], E_SignalKind.DO)
                AND (NOT _bBistable OR F_SignalValid(_refCfg.aSignal[2], E_SignalKind.DO));
END_METHOD

METHOD PROTECTED _OnStart : E_ActStatus
VAR_INPUT
    nAction : UINT;
END_VAR
_fMove := 0;
CASE nAction OF
    GVL_Catalog.Gripper_Ouvrir:
        _bCmdClose := FALSE;
        _bHolding := FALSE;
        _OnStart := E_ActStatus.Busy;
    GVL_Catalog.Gripper_Fermer, GVL_Catalog.Gripper_SerrerSurPiece:
        _bCmdClose := TRUE;
        _OnStart := E_ActStatus.Busy;
    GVL_Catalog.Gripper_VerifierPresencePiece:
        _SetResult(1, BOOL_TO_LREAL(_PartPresent()));
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
_fMove := _fMove + F_CycleMs();
IF _bCmdClose THEN
    bReached := SEL(_bHasClosed, _fMove >= _refCfg.aParam[2], _bClosed);
ELSE
    bReached := SEL(_bHasOpen, _fMove >= _refCfg.aParam[2], _bOpen);
END_IF
IF NOT bReached THEN
    _OnCycle := E_ActStatus.Busy;
    RETURN;
END_IF
IF nAction = GVL_Catalog.Gripper_SerrerSurPiece THEN
    _SetResult(1, BOOL_TO_LREAL(_PartPresent()));
    IF NOT _PartPresent() THEN
        _Fail(GVL_Catalog.Gripper_Err100, 0);       // pièce absente à la fermeture
        _OnCycle := E_ActStatus.Error;
        RETURN;
    END_IF
    _bHolding := TRUE;
END_IF
_OnCycle := E_ActStatus.Done;
END_METHOD

METHOD PRIVATE _PartPresent : BOOL
// Avec un capteur de présence : le capteur ; sinon, fermée sans atteindre la butée « fermée » = sur pièce
IF _bHasPart THEN
    _PartPresent := _bPart;
ELSE
    _PartPresent := _bCmdClose AND NOT _bOpen;
END_IF
END_METHOD

METHOD PROTECTED _Monitor
IF _bHasClosed AND _bHasOpen AND _bClosed AND _bOpen THEN
    IF _eState <> E_ObjState.Error THEN _Fail(GVL_Catalog.Gripper_Err102, 0); END_IF
ELSIF _bHolding AND _bHasPart AND NOT _bPart AND _eState <> E_ObjState.Error THEN
    _bHolding := FALSE;
    _Fail(GVL_Catalog.Gripper_Err101, 0);          // pièce perdue pendant le maintien
END_IF
END_METHOD

// État sûr : maintien (docs/03, catalogue)
METHOD PROTECTED _SafeState
;
END_METHOD

METHOD PROTECTED _ReadHw
_bClosed := F_ReadDI(_refCfg.aSignal[3]);
_bOpen := F_ReadDI(_refCfg.aSignal[4]);
_bPart := F_ReadDI(_refCfg.aSignal[5]);
END_METHOD

METHOD PROTECTED _Simulate
VAR
    fStep : LREAL;
END_VAR
fStep := F_CycleMs() / MAX(_refCfg.aParam[2], 10.0);
IF _bCmdClose THEN _fSimPos := MIN(1.0, _fSimPos + fStep); ELSE _fSimPos := MAX(0.0, _fSimPos - fStep); END_IF
_bOpen := _fSimPos <= 0.0;
_bClosed := _fSimPos >= 1.0;
_bPart := _bSimPart AND _bCmdClose AND _fSimPos >= 1.0;
IF _Forced(3) THEN _bClosed := _ForcedValue(3) >= 0.5; END_IF
IF _Forced(4) THEN _bOpen := _ForcedValue(4) >= 0.5; END_IF
IF _Forced(5) THEN _bPart := _ForcedValue(5) >= 0.5; END_IF
END_METHOD

METHOD PROTECTED _WriteHw
F_WriteDO(_refCfg.aSignal[1], _bCmdClose);
IF _bBistable THEN F_WriteDO(_refCfg.aSignal[2], NOT _bCmdClose); END_IF
END_METHOD

METHOD PROTECTED _Publish
// 0 Inconnu, 1 Ouverte, 2 Fermée, 3 Fermée sur pièce
IF _bCmdClose AND _PartPresent() THEN
    _stStatus.aValue[1] := 3;
ELSIF _bCmdClose AND (_bClosed OR NOT _bHasClosed) THEN
    _stStatus.aValue[1] := 2;
ELSIF NOT _bCmdClose AND (_bOpen OR NOT _bHasOpen) THEN
    _stStatus.aValue[1] := 1;
ELSE
    _stStatus.aValue[1] := 0;
END_IF
_stStatus.aValue[2] := BOOL_TO_LREAL(_PartPresent());
END_METHOD
END_FUNCTION_BLOCK
