// Gestionnaire de movers (docs/05 § 6) : occupation et réservation des positions planaires, envoi des
// déplacements au pilote planaire, détection des arrivées (fenêtre de position et à l'arrêt 50 ms), timeout.
FUNCTION_BLOCK FB_MoverManager IMPLEMENTS I_MoverMotion
VAR
    _refMachine : REFERENCE TO ST_MachineData;
    ipDriver : I_PlanarDriver;
    aOwner : ARRAY[1..GVL_Const.MAX_PLANAR_POS] OF USINT;         // mover propriétaire, 0 = libre
    _aDest : ARRAY[1..GVL_Const.MAX_MOVERS] OF INT;
    _aMoveMs : ARRAY[1..GVL_Const.MAX_MOVERS] OF LREAL;
    _aStillMs : ARRAY[1..GVL_Const.MAX_MOVERS] OF LREAL;
    _aTimeoutMs : ARRAY[1..GVL_Const.MAX_MOVERS] OF LREAL;
    _aArrived : ARRAY[1..GVL_Const.MAX_MOVERS] OF INT;           // position atteinte ce cycle, 0 sinon
    _aTimedOut : ARRAY[1..GVL_Const.MAX_MOVERS] OF BOOL;
    _fVelocity : LREAL := 1000.0;
    _fAcceleration : LREAL := 5000.0;
    _fWindow : LREAL := 0.1;
END_VAR

METHOD Init
VAR_INPUT
    refMachine : REFERENCE TO ST_MachineData;
    ipPlanar : I_PlanarDriver;
END_VAR
_refMachine REF= refMachine;
ipDriver := ipPlanar;
END_METHOD

// Paramètres du système planaire (type 13 : P1 vitesse, P2 accélération, P3 fenêtre)
METHOD Configure
VAR
    i : UINT;
END_VAR
FOR i := 1 TO _refMachine.nMachineObj DO
    IF _refMachine.aMachineObj[i].nType = GVL_Catalog.Type_PlanarSystem THEN
        IF _refMachine.aMachineObj[i].aParam[1] > 0 THEN _fVelocity := _refMachine.aMachineObj[i].aParam[1]; END_IF
        IF _refMachine.aMachineObj[i].aParam[2] > 0 THEN _fAcceleration := _refMachine.aMachineObj[i].aParam[2]; END_IF
        IF _refMachine.aMachineObj[i].aParam[3] > 0 THEN _fWindow := _refMachine.aMachineObj[i].aParam[3]; END_IF
    END_IF
END_FOR
END_METHOD

METHOD Owner : USINT
VAR_INPUT
    nPos : INT;
END_VAR
IF nPos >= 1 AND nPos <= GVL_Const.MAX_PLANAR_POS THEN Owner := aOwner[nPos]; END_IF
END_METHOD

METHOD Reserve
VAR_INPUT
    nPos : INT;
    nMover : USINT;
END_VAR
IF nPos >= 1 AND nPos <= GVL_Const.MAX_PLANAR_POS THEN aOwner[nPos] := nMover; END_IF
END_METHOD

METHOD Free
VAR_INPUT
    nPos : INT;
    nMover : USINT;
END_VAR
IF nPos >= 1 AND nPos <= GVL_Const.MAX_PLANAR_POS AND_THEN aOwner[nPos] = nMover THEN aOwner[nPos] := 0; END_IF
END_METHOD

// Libère toutes les positions d'un mover sauf nKeep
METHOD FreeAll
VAR_INPUT
    nMover : USINT;
    nKeep : INT;
END_VAR
VAR
    i : INT;
END_VAR
FOR i := 1 TO GVL_Const.MAX_PLANAR_POS DO
    IF aOwner[i] = nMover AND i <> nKeep THEN aOwner[i] := 0; END_IF
END_FOR
END_METHOD

// Réserve la destination puis envoie le déplacement (docs/05 § 6.2)
METHOD MoveTo : BOOL
VAR_INPUT
    nMover : USINT;
    nPos : INT;
END_VAR
VAR
    stCur : ST_Position;
    fDist : LREAL;
END_VAR
MoveTo := FALSE;
IF ipDriver = 0 OR nPos < 1 OR nPos > _refMachine.nPlanarPos OR nMover < 1 OR nMover > GVL_Const.MAX_MOVERS THEN RETURN; END_IF
IF aOwner[nPos] <> 0 AND aOwner[nPos] <> nMover THEN RETURN; END_IF
IF NOT ipDriver.MoveTo(nMover, _refMachine.aPlanarPos[nPos].stPos, _fVelocity, _fAcceleration) THEN RETURN; END_IF
FreeAll(nMover, 0);
aOwner[nPos] := nMover;
_aDest[nMover] := nPos;
_aMoveMs[nMover] := 0;
_aStillMs[nMover] := 0;
_aArrived[nMover] := 0;
_aTimedOut[nMover] := FALSE;
stCur := ipDriver.MoverPosition(nMover);
fDist := SQRT(EXPT(_refMachine.aPlanarPos[nPos].stPos.fX - stCur.fX, 2) + EXPT(_refMachine.aPlanarPos[nPos].stPos.fY - stCur.fY, 2));
_aTimeoutMs[nMover] := 2.0 * (fDist / _fVelocity + _fVelocity / _fAcceleration) * 1000.0 + 5000.0;
MoveTo := TRUE;
END_METHOD

METHOD Cycle
VAR_INPUT
    fOverride : LREAL;
END_VAR
VAR
    i : USINT;
    stPos : ST_Position;
    nPos : INT;
    fDt : LREAL;
END_VAR
IF ipDriver = 0 THEN RETURN; END_IF
ipDriver.Cycle(fOverride);
fDt := F_CycleMs();
FOR i := 1 TO _refMachine.nMovers DO
    _aArrived[i] := 0;
    nPos := _aDest[i];
    IF nPos > 0 THEN
        stPos := ipDriver.MoverPosition(i);
        IF fOverride > 0 THEN _aMoveMs[i] := _aMoveMs[i] + fDt * fOverride / 100.0; END_IF
        IF NOT ipDriver.MoverMoving(i)
           AND ABS(stPos.fX - _refMachine.aPlanarPos[nPos].stPos.fX) <= MAX(_fWindow, 0.01)
           AND ABS(stPos.fY - _refMachine.aPlanarPos[nPos].stPos.fY) <= MAX(_fWindow, 0.01) THEN
            _aStillMs[i] := _aStillMs[i] + fDt;
            IF _aStillMs[i] >= GVL_Const.ARRIVAL_SETTLE_MS THEN
                _aArrived[i] := nPos;
                _aDest[i] := 0;
            END_IF
        ELSE
            _aStillMs[i] := 0;
            IF _aMoveMs[i] > _aTimeoutMs[i] THEN
                _aTimedOut[i] := TRUE;
                _aDest[i] := 0;
            END_IF
        END_IF
    END_IF
END_FOR
END_METHOD

// Position atteinte ce cycle (0 sinon)
METHOD Arrived : INT
VAR_INPUT
    nMover : USINT;
END_VAR
IF nMover >= 1 AND nMover <= GVL_Const.MAX_MOVERS THEN Arrived := _aArrived[nMover]; END_IF
END_METHOD

METHOD TimedOut : BOOL
VAR_INPUT
    nMover : USINT;
END_VAR
TimedOut := nMover >= 1 AND nMover <= GVL_Const.MAX_MOVERS AND_THEN _aTimedOut[nMover];
END_METHOD

METHOD Moving : BOOL
VAR_INPUT
    nMover : USINT;
END_VAR
Moving := nMover >= 1 AND nMover <= GVL_Const.MAX_MOVERS AND_THEN (_aDest[nMover] <> 0 OR (ipDriver <> 0 AND_THEN ipDriver.MoverMoving(nMover)));
END_METHOD

// Mode manuel (docs/05 § 6.5) : le mover quitte ses positions ; arrivé sur une position planaire libre, il la tient
METHOD MoveFree : BOOL
VAR_INPUT
    nMover : USINT;
    stPos : ST_Position;
    fSpeedPct : LREAL;
END_VAR
VAR
    k : INT;
END_VAR
MoveFree := FALSE;
IF ipDriver = 0 OR nMover < 1 OR nMover > GVL_Const.MAX_MOVERS THEN RETURN; END_IF
// Une position planaire tenue par un autre mover ne peut pas être visée
FOR k := 1 TO _refMachine.nPlanarPos DO
    IF _Near(stPos, _refMachine.aPlanarPos[k].stPos) AND aOwner[k] <> 0 AND aOwner[k] <> nMover THEN RETURN; END_IF
END_FOR
IF NOT ipDriver.MoveTo(nMover, stPos, _fVelocity * LIMIT(1.0, fSpeedPct, 100.0) / 100.0, _fAcceleration) THEN RETURN; END_IF
FreeAll(nMover, 0);
_aDest[nMover] := 0;
_aTimedOut[nMover] := FALSE;
FOR k := 1 TO _refMachine.nPlanarPos DO
    IF _Near(stPos, _refMachine.aPlanarPos[k].stPos) THEN aOwner[k] := nMover; END_IF
END_FOR
MoveFree := TRUE;
END_METHOD

METHOD StopMover
VAR_INPUT
    nMover : USINT;
END_VAR
IF ipDriver <> 0 THEN ipDriver.StopMover(nMover); END_IF
IF nMover >= 1 AND nMover <= GVL_Const.MAX_MOVERS THEN _aDest[nMover] := 0; END_IF
END_METHOD

METHOD Position : ST_Position
VAR_INPUT
    nMover : USINT;
END_VAR
IF ipDriver <> 0 THEN Position := ipDriver.MoverPosition(nMover); END_IF
END_METHOD

METHOD PRIVATE _Near : BOOL
VAR_INPUT
    a : ST_Position;
    b : ST_Position;
END_VAR
_Near := ABS(a.fX - b.fX) <= MAX(_fWindow, 0.5) AND ABS(a.fY - b.fY) <= MAX(_fWindow, 0.5);
END_METHOD

METHOD AnyMoving : BOOL
VAR
    i : USINT;
END_VAR
AnyMoving := FALSE;
FOR i := 1 TO GVL_Const.MAX_MOVERS DO
    IF Moving(i) THEN AnyMoving := TRUE; END_IF
END_FOR
END_METHOD

// Arrêt contrôlé de tous les movers (Abort) : les destinations sont abandonnées, les réservations gardées
METHOD StopAll
VAR
    i : USINT;
END_VAR
IF ipDriver <> 0 THEN ipDriver.StopAll(); END_IF
FOR i := 1 TO GVL_Const.MAX_MOVERS DO _aDest[i] := 0; END_FOR
END_METHOD

// Position planaire connue où se trouve le mover (Reset machine, docs/05 § 6.3), 0 sinon
METHOD PositionAt : INT
VAR_INPUT
    nMover : USINT;
END_VAR
VAR
    i : INT;
    stPos : ST_Position;
END_VAR
PositionAt := 0;
IF ipDriver = 0 THEN RETURN; END_IF
stPos := ipDriver.MoverPosition(nMover);
FOR i := 1 TO _refMachine.nPlanarPos DO
    IF ABS(stPos.fX - _refMachine.aPlanarPos[i].stPos.fX) <= MAX(_fWindow, 0.5) AND ABS(stPos.fY - _refMachine.aPlanarPos[i].stPos.fY) <= MAX(_fWindow, 0.5) THEN
        PositionAt := i;
        RETURN;
    END_IF
END_FOR
END_METHOD

// Position tampon libre la plus proche du mover, 0 si aucune
METHOD NearestFree : INT
VAR_INPUT
    nMover : USINT;
    eKind : E_PlanarPosKind;
END_VAR
VAR
    i : INT;
    stPos : ST_Position;
    fD : LREAL;
    fBest : LREAL := 1.0E30;
END_VAR
NearestFree := 0;
IF ipDriver = 0 THEN RETURN; END_IF
stPos := ipDriver.MoverPosition(nMover);
FOR i := 1 TO _refMachine.nPlanarPos DO
    IF _refMachine.aPlanarPos[i].eKind = eKind AND (aOwner[i] = 0 OR aOwner[i] = nMover) THEN
        fD := ABS(_refMachine.aPlanarPos[i].stPos.fX - stPos.fX) + ABS(_refMachine.aPlanarPos[i].stPos.fY - stPos.fY);
        IF fD < fBest THEN
            fBest := fD;
            NearestFree := i;
        END_IF
    END_IF
END_FOR
END_METHOD

PROPERTY Driver : I_PlanarDriver
GET
Driver := ipDriver;
END_GET
END_PROPERTY
END_FUNCTION_BLOCK
