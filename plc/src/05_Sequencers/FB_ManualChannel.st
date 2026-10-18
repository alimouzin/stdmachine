// Canal manuel d'une station (docs/05 § 7) : action, jog avec garde, acquittement, apprentissage.
// Conditions : mode Maintenance et station Stopped ; les droits sont contrôlés par la HMI.
FUNCTION_BLOCK FB_ManualChannel
VAR
    _nStation : USINT;
    _refStation : REFERENCE TO FB_Station;
    _nLastSeq : UDINT;
    _nObject : UINT;                // objet de l'action en cours, 0 = aucune
    _bJog : BOOL;
    _nLastAlive : UDINT;
    _fQuietMs : LREAL;
    _eStateAtStart : E_State;
END_VAR

METHOD Init
VAR_INPUT
    nStation : USINT;
    refStation : REFERENCE TO FB_Station;
END_VAR
_nStation := nStation;
_refStation REF= refStation;
END_METHOD

METHOD Cycle
VAR
    req : REFERENCE TO ST_ManualRequest;
    resp : REFERENCE TO ST_ManualResponse;
    ipObj : I_Object;
END_VAR
IF NOT __ISVALIDREF(_refStation) OR _nStation < 1 OR _nStation > GVL_Const.MAX_STATIONS THEN RETURN; END_IF
req REF= GVL_Hmi.aManual[_nStation];
resp REF= GVL_Hmi.aManualResp[_nStation];

// Suivi de l'action en cours
IF _nObject <> 0 THEN
    ipObj := _refStation.aObj[_nObject];
    IF ipObj <> 0 THEN
        resp.eActStatus := ipObj.ActStatus;
        resp.nErrorCode := ipObj.Status.nErrorCode;
        resp.nSubCode := ipObj.Status.nSubCode;
        resp.stResults := ipObj.Results;
        IF _bJog THEN
            // Garde du jog : la HMI incrémente nJogAlive toutes les 100 ms bouton tenu
            IF GVL_Hmi.nJogAlive <> _nLastAlive THEN
                _nLastAlive := GVL_Hmi.nJogAlive;
                _fQuietMs := 0;
            ELSE
                _fQuietMs := _fQuietMs + F_CycleMs();
            END_IF
            IF _fQuietMs >= GVL_Const.JOG_GUARD_MS OR _refStation.State <> _eStateAtStart THEN
                ipObj.Abort();
                _bJog := FALSE;
                _nObject := 0;
            END_IF
        ELSIF ipObj.ActStatus <> E_ActStatus.Busy THEN
            _nObject := 0;
        END_IF
    ELSE
        _nObject := 0;
    END_IF
END_IF

IF req.nSeq = _nLastSeq THEN RETURN; END_IF
_nLastSeq := req.nSeq;
resp.nSeq := req.nSeq;
resp.eReason := E_RejectReason.None;
resp.eResult := E_CmdResult.Rejected;
IF _refStation.Mode <> E_Mode.Maintenance THEN resp.eReason := E_RejectReason.WrongMode; RETURN; END_IF
IF _refStation.State <> E_State.Stopped THEN resp.eReason := E_RejectReason.WrongState; RETURN; END_IF
IF req.eKind <> E_ManualKind.TeachPosition AND (req.nObject < 1 OR req.nObject > GVL_Const.MAX_OBJ_PER_STATION) THEN
    resp.eReason := E_RejectReason.NotConfigured;
    RETURN;
END_IF
IF req.eKind <> E_ManualKind.TeachPosition THEN
    ipObj := _refStation.aObj[req.nObject];
    IF ipObj = 0 THEN resp.eReason := E_RejectReason.NotConfigured; RETURN; END_IF
END_IF

CASE req.eKind OF
    E_ManualKind.Action, E_ManualKind.JogStart:
        IF _nObject <> 0 THEN resp.eReason := E_RejectReason.Busy; RETURN; END_IF
        resp.eStart := ipObj.Start(req.nAction, req.stParams, TRUE);
        IF resp.eStart = E_StartResult.Accepted THEN
            _nObject := req.nObject;
            _bJog := req.eKind = E_ManualKind.JogStart;
            _nLastAlive := GVL_Hmi.nJogAlive;
            _fQuietMs := 0;
            _eStateAtStart := _refStation.State;
            resp.eResult := E_CmdResult.Accepted;
        END_IF
        resp.eActStatus := ipObj.ActStatus;
        resp.stResults := ipObj.Results;
    E_ManualKind.JogStop:
        ipObj.Abort();
        _bJog := FALSE;
        _nObject := 0;
        resp.eResult := E_CmdResult.Accepted;
    E_ManualKind.Ack:
        ipObj.Ack();
        resp.eResult := E_CmdResult.Accepted;
    E_ManualKind.TeachPosition:
        resp.eResult := SEL(_Teach(req := req, stTaught => resp.stTaught), E_CmdResult.Rejected, E_CmdResult.Accepted);
        IF resp.eResult = E_CmdResult.Rejected THEN resp.eReason := E_RejectReason.NotConfigured; END_IF
END_CASE
END_METHOD

// Apprentissage : position actuelle des axes indiqués, composantes choisies (docs/05 § 7)
METHOD PRIVATE _Teach : BOOL
VAR_INPUT
    req : REFERENCE TO ST_ManualRequest;
END_VAR
VAR_OUTPUT
    stTaught : ST_Position;
END_VAR
VAR
    k : USINT;
    n : UINT;
    fValue : LREAL;
END_VAR
_Teach := FALSE;
IF req.nTeachPos < 1 OR req.nTeachPos > GVL_Const.MAX_NAMED_POS THEN RETURN; END_IF
stTaught := _refStation.Data.aPos[req.nTeachPos].stPos;
FOR k := 1 TO 4 DO
    n := req.aTeachFrom[k];
    IF (SHR(req.nTeachMask, k - 1) AND 1) = 1 AND n >= 1 AND n <= GVL_Const.MAX_OBJ_PER_STATION THEN
        IF _refStation.aObj[n] <> 0 THEN
            fValue := _refStation.aObj[n].Status.aValue[1];
            CASE k OF
                1: stTaught.fX := fValue;
                2: stTaught.fY := fValue;
                3: stTaught.fZ := fValue;
                4: stTaught.fC := fValue;
            END_CASE
        END_IF
    END_IF
END_FOR
_refStation.Data.aPos[req.nTeachPos].stPos := stTaught;
_refStation.Data.aPos[req.nTeachPos].bTaught := TRUE;
_Teach := TRUE;
END_METHOD
END_FUNCTION_BLOCK
