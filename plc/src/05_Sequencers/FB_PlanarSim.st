// Système planaire simulé (docs/06 § 8) : déplacement en ligne droite, profil en S, arrêt si la destination est occupée
// (l'occupation est gérée par FB_MoverManager, qui ne donne jamais une destination déjà prise).
FUNCTION_BLOCK FB_PlanarSim IMPLEMENTS I_PlanarDriver
VAR
    nMovers : USINT;
    _bActive : BOOL;
    _fActivateMs : LREAL;
    _aPos : ARRAY[1..GVL_Const.MAX_MOVERS] OF ST_Position;
    _aFrom : ARRAY[1..GVL_Const.MAX_MOVERS] OF ST_Position;
    _aTo : ARRAY[1..GVL_Const.MAX_MOVERS] OF ST_Position;
    _aT : ARRAY[1..GVL_Const.MAX_MOVERS] OF LREAL;          // 0..1 avancement
    _aDurMs : ARRAY[1..GVL_Const.MAX_MOVERS] OF LREAL;
    _aMoving : ARRAY[1..GVL_Const.MAX_MOVERS] OF BOOL;
    _aAbsent : ARRAY[1..GVL_Const.MAX_MOVERS] OF BOOL;      // essai : mover retiré du plan
    _nError : UINT;
END_VAR

METHOD Activate : BOOL
VAR_INPUT
    bEnable : BOOL;
END_VAR
IF NOT bEnable THEN
    _bActive := FALSE;
    _fActivateMs := 0;
ELSIF NOT _bActive THEN
    _fActivateMs := _fActivateMs + F_CycleMs();
    IF _fActivateMs >= 1500.0 THEN _bActive := TRUE; END_IF
END_IF
Activate := _bActive;
END_METHOD

METHOD MoveTo : BOOL
VAR_INPUT
    nMover : USINT;
    stPos : ST_Position;
    fVelocity : LREAL;
    fAcceleration : LREAL;
END_VAR
VAR
    fDist : LREAL;
    fV : LREAL;
    fA : LREAL;
    fAccDist : LREAL;
END_VAR
MoveTo := FALSE;
IF NOT _bActive OR nMover < 1 OR nMover > nMovers OR_ELSE _aAbsent[nMover] THEN RETURN; END_IF
_aFrom[nMover] := _aPos[nMover];
_aTo[nMover] := stPos;
fDist := SQRT((stPos.fX - _aPos[nMover].fX) * (stPos.fX - _aPos[nMover].fX) + (stPos.fY - _aPos[nMover].fY) * (stPos.fY - _aPos[nMover].fY));
fV := MAX(fVelocity, 1.0);
fA := MAX(fAcceleration, 1.0);
fAccDist := fV * fV / fA;
IF fDist >= fAccDist THEN
    _aDurMs[nMover] := ((fDist - fAccDist) / fV + 2.0 * fV / fA) * 1000.0;
ELSE
    _aDurMs[nMover] := 2.0 * SQRT(fDist / fA) * 1000.0;
END_IF
_aDurMs[nMover] := MAX(_aDurMs[nMover], 50.0);
_aT[nMover] := 0;
_aMoving[nMover] := TRUE;
MoveTo := TRUE;
END_METHOD

METHOD StopAll
VAR
    i : USINT;
END_VAR
FOR i := 1 TO GVL_Const.MAX_MOVERS DO _aMoving[i] := FALSE; END_FOR
END_METHOD

METHOD AckError
_nError := 0;
END_METHOD

METHOD Cycle
VAR_INPUT
    fOverride : LREAL;
END_VAR
VAR
    i : USINT;
    e : LREAL;
END_VAR
FOR i := 1 TO nMovers DO
    IF _aMoving[i] AND _bActive THEN
        _aT[i] := MIN(1.0, _aT[i] + F_CycleMs() * LIMIT(0.0, fOverride, 100.0) / 100.0 / _aDurMs[i]);
        e := _aT[i] * _aT[i] * (3.0 - 2.0 * _aT[i]);
        _aPos[i].fX := _aFrom[i].fX + (_aTo[i].fX - _aFrom[i].fX) * e;
        _aPos[i].fY := _aFrom[i].fY + (_aTo[i].fY - _aFrom[i].fY) * e;
        _aPos[i].fC := _aTo[i].fC;
        IF _aT[i] >= 1.0 THEN
            _aPos[i] := _aTo[i];
            _aMoving[i] := FALSE;
        END_IF
    END_IF
END_FOR
END_METHOD

METHOD MoverPosition : ST_Position
VAR_INPUT
    nMover : USINT;
END_VAR
IF nMover >= 1 AND nMover <= GVL_Const.MAX_MOVERS THEN MoverPosition := _aPos[nMover]; END_IF
END_METHOD

METHOD MoverMoving : BOOL
VAR_INPUT
    nMover : USINT;
END_VAR
MoverMoving := nMover >= 1 AND nMover <= GVL_Const.MAX_MOVERS AND_THEN _aMoving[nMover];
END_METHOD

METHOD MoverDetected : BOOL
VAR_INPUT
    nMover : USINT;
END_VAR
MoverDetected := _bActive AND nMover >= 1 AND nMover <= nMovers AND_THEN NOT _aAbsent[nMover];
END_METHOD

METHOD SetInitialPosition
VAR_INPUT
    nMover : USINT;
    stPos : ST_Position;
END_VAR
IF nMover >= 1 AND nMover <= GVL_Const.MAX_MOVERS THEN _aPos[nMover] := stPos; END_IF
END_METHOD

// Essai : retirer un mover du plan, ou provoquer une collision (docs/12 § 5)
METHOD SetAbsent
VAR_INPUT
    nMover : USINT;
    bAbsent : BOOL;
END_VAR
IF nMover >= 1 AND nMover <= GVL_Const.MAX_MOVERS THEN _aAbsent[nMover] := bAbsent; END_IF
END_METHOD

METHOD SetError
VAR_INPUT
    nCode : UINT;
END_VAR
_nError := nCode;
END_METHOD

// Nombre de movers posés sur le plan simulé (configuration machine)
METHOD SetMoverCount
VAR_INPUT
    nCount : USINT;
END_VAR
nMovers := MIN(nCount, GVL_Const.MAX_MOVERS);
END_METHOD

PROPERTY Active : BOOL
GET
Active := _bActive;
END_GET
END_PROPERTY

PROPERTY ErrorCode : UINT
GET
ErrorCode := _nError;
END_GET
END_PROPERTY

PROPERTY ErrorSubCode : UDINT
GET
ErrorSubCode := 0;
END_GET
END_PROPERTY
END_FUNCTION_BLOCK
