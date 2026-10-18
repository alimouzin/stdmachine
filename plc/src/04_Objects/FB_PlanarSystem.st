// Système planaire, type 13, niveau machine (docs/03 § 4.3) : activation, désactivation, acquittement du pilote
// planaire (GVL_IoRef.ipPlanar). Les erreurs du système (100 à 102) sont levées par FB_Machine, qui tient l'état.
FUNCTION_BLOCK FB_PlanarSystem EXTENDS FB_ObjectBase
VAR
    _nDetected : INT;
END_VAR

METHOD PROTECTED _OnStart : E_ActStatus
VAR_INPUT
    nAction : UINT;
END_VAR
IF GVL_IoRef.ipPlanar = 0 THEN
    _Fail(GVL_Catalog.Err_NotWired, 0);
    _OnStart := E_ActStatus.Error;
    RETURN;
END_IF
CASE nAction OF
    GVL_Catalog.PlanarSystem_ActiverLeSysteme, GVL_Catalog.PlanarSystem_DesactiverLeSysteme, GVL_Catalog.PlanarSystem_AcquitterErreur:
        _OnStart := _OnCycle(nAction);
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
    GVL_Catalog.PlanarSystem_ActiverLeSysteme:
        IF GVL_IoRef.ipPlanar.Activate(TRUE) THEN
            _SetResult(1, INT_TO_LREAL(_nDetected));
            _OnCycle := E_ActStatus.Done;
        END_IF
    GVL_Catalog.PlanarSystem_DesactiverLeSysteme:
        GVL_IoRef.ipPlanar.Activate(FALSE);
        IF NOT GVL_IoRef.ipPlanar.Active THEN _OnCycle := E_ActStatus.Done; END_IF
    GVL_Catalog.PlanarSystem_AcquitterErreur:
        GVL_IoRef.ipPlanar.AckError();
        IF GVL_IoRef.ipPlanar.ErrorCode = 0 THEN _OnCycle := E_ActStatus.Done; END_IF
ELSE
    _OnCycle := E_ActStatus.Error;
END_CASE
END_METHOD

METHOD PROTECTED _Publish
VAR
    i : USINT;
END_VAR
_nDetected := 0;
IF GVL_IoRef.ipPlanar = 0 THEN RETURN; END_IF
FOR i := 1 TO GVL_Const.MAX_MOVERS DO
    IF GVL_IoRef.ipPlanar.MoverDetected(i) THEN _nDetected := _nDetected + 1; END_IF
END_FOR
_stStatus.aValue[1] := SEL(GVL_IoRef.ipPlanar.Active, 0.0, 1.0);
_stStatus.aValue[2] := INT_TO_LREAL(_nDetected);
END_METHOD
END_FUNCTION_BLOCK
