// Bloc de tête du PLC (docs/05 § 5.1, § 5.2, § 10 ; docs/06 § 6) : machine d'états de la machine, propagation des
// commandes aux stations, défauts machine et raison d'arrêt, objets de niveau machine, système planaire, séquenceur
// général, calibrations machine, chargeur, handshakes de GVL_Hmi, forçages, surveillance des rebuts, balise,
// identité du PLC, battement de la HMI, publication du statut.
//
// Le projet de la machine l'appelle une fois par cycle dans MAIN : fbMachine(); après avoir renseigné GVL_IoRef.
// Avant le premier appel, il peut donner ses pools (SetPools) et son pilote planaire (SetPlanarDriver) ; sinon la
// bibliothèque prend FB_DefaultPools et le système planaire simulé.
FUNCTION_BLOCK FB_Machine EXTENDS FB_NodeBase IMPLEMENTS I_ManualHost, I_ObjectHost
VAR
    fbPack : FB_PackML;
    aStation : ARRAY[1..GVL_Const.MAX_STATIONS] OF FB_Station;
    fbSeq : FB_GeneralSequencer;
    fbMgr : FB_MoverManager;
    fbPlanarSim : FB_PlanarSim;
    fbAlarms : FB_AlarmManager;
    fbTraces : FB_TraceBuffer;
    fbQuality : FB_QualityMonitor;
    fbFactory : FB_ObjectFactory;
    fbLoader : FB_Loader;
    fbCalib : FB_MachineCalib;
    fbDefaultPools : FB_DefaultPools;
    fbManual : FB_ManualChannel;            // canal 0 : objets de niveau machine
    _ipPools : I_ObjectPools;
    _ipPlanar : I_PlanarDriver;
    _bSimPlanar : BOOL;
    aObj : ARRAY[1..GVL_Const.MAX_MACHINE_OBJ] OF I_Object;
    _nObj : UINT;
    _bInit : BOOL;
    _ePendingCmd : E_Cmd;
    _sCmdUser : STRING(24);
    _eReasonNext : E_StopReason;            // cause système du prochain arrêt
    _bOperatorAwaited : BOOL;               // fin d'OF, machine vidée : bleu clignotant jusqu'au prochain Start
    // Handshakes (docs/06 § 6.1) : dernier nSeq traité
    _nCmdSeq : UDINT;
    _nOverrideSeq : UDINT;
    _nLaunchSeq : UDINT;
    _nOrderSeq : UDINT;
    _nMoverVarSeq : UDINT;
    _nPromptSeq : UDINT;
    _nPartSeq : UDINT;
    _nAckSeq : UDINT;
    _nForceSeq : UDINT;
    _nQualitySeq : UDINT;
    _nSimForceSeq : UDINT;
    // Battement de la HMI
    _nLastHmiBeat : UDINT;
    _fHmiQuietMs : LREAL;
    bHmiLost : BOOL;
    _nHeartbeat : UDINT;
    _fRetainedMs : LREAL;
    _tLastCalibCheck : LINT;
    _nPlanarError : UINT;
END_VAR
VAR PERSISTENT
    stForce : ST_MachineForce;              // forçages et état déclaré (docs/12)
    _fMachineOverride : LREAL := 100.0;
    _aStationOverride : ARRAY[1..GVL_Const.MAX_STATIONS] OF LREAL := [16(100.0)];
    _eMode : E_Mode;
    _nStopCauseGid : UDINT;
    _nStopCauseCode : UINT;
    _eStopReason : E_StopReason;
END_VAR

_Run();

// ================================================================== mise en place

METHOD SetPools
VAR_INPUT
    ipPools : I_ObjectPools;
END_VAR
_ipPools := ipPools;
END_METHOD

METHOD SetPlanarDriver
VAR_INPUT
    ipDriver : I_PlanarDriver;
END_VAR
_ipPlanar := ipDriver;
END_METHOD

METHOD PRIVATE _Init
VAR
    n : USINT;
    t : UINT;
    stResp : ST_DeployResponse;
END_VAR
fbAlarms.Bind(GVL_Hmi.stAlarms);
fbTraces.Bind(GVL_Hmi.stTraces);
IF _ipPools = 0 THEN _ipPools := fbDefaultPools; END_IF
IF _ipPlanar = 0 THEN
    _ipPlanar := fbPlanarSim;
    _bSimPlanar := TRUE;
END_IF
fbFactory.SetPools(_ipPools, fbAlarms);
fbFactory.SetHost(0, THIS^, THIS^, 0);
FOR n := 1 TO GVL_Const.MAX_STATIONS DO
    aStation[n].Init(nStation := n, refData := GVL_Active.aStation[n], refMachine := GVL_Active.stMachine, refAlarms := fbAlarms,
                     refStatus := GVL_Hmi.aStationStatus[n], refStep := GVL_Hmi.aStep[n], ipMachine := THIS^);
    aStation[n].SetOverride(_aStationOverride[n]);
    fbFactory.SetHost(n, aStation[n], aStation[n], aStation[n]);
    fbSeq.SetStation(n, ADR(aStation[n]));
    fbCalib.SetStation(n, ADR(aStation[n]));
END_FOR
fbQuality.Init(GVL_Active.stMachine, fbAlarms);
fbMgr.Init(GVL_Active.stMachine, _ipPlanar);
GVL_IoRef.ipPlanar := _ipPlanar;
GVL_IoRef.ipMovers := fbMgr;
fbSeq.Init(GVL_Active.stMachine, fbMgr, fbQuality, fbAlarms, fbTraces);
fbCalib.Init(GVL_Active.stMachine, fbAlarms, fbMgr);
fbManual.Init(nStation := 0, ipHost := THIS^);
fbLoader.Init(fbFactory);
SetOverride(_fMachineOverride);

// Mémoire rémanente : identifiant tiré à sa première initialisation (docs/02 § 7.4)
IF GVL_Active.nRetainId = 0 THEN
    GVL_Active.nRetainId := LINT_TO_ULINT(F_NowUtc()) XOR 16#9E3779B97F4A7C15;
END_IF

// Redémarrage du PLC : le dernier déploiement reste actif, les objets sont rattachés de nouveau à leurs éléments
IF GVL_Active.bDeployed THEN
    fbFactory.Build(stResp);
    fbMgr.Configure();
    fbSeq.ScanGroups();
    _PlaceSimMovers();
END_IF
FOR n := 1 TO GVL_Const.MAX_STATIONS DO aStation[n].SetMode(_eMode); END_FOR

// Identité (docs/05 § 10)
GVL_Hmi.stIdentity.nInterfaceVersion := GVL_Const.INTERFACE_VERSION;
GVL_Hmi.stIdentity.nInstrSetVersion := GVL_Const.INSTR_SET_VERSION;
GVL_Hmi.stIdentity.nCatalogVersion := GVL_Catalog.CatalogVersion;
GVL_Hmi.stIdentity.sLibVersion := GVL_Const.LIB_VERSION;
GVL_Hmi.stIdentity.nRetainId := GVL_Active.nRetainId;
FOR t := 1 TO GVL_Const.MAX_TYPE DO GVL_Hmi.stIdentity.aPoolSize[t] := fbFactory.PoolSize(t); END_FOR

// Les demandes déjà présentes dans GVL_Hmi au démarrage ne sont pas rejouées
_nCmdSeq := GVL_Hmi.stCmd.nSeq;
_nOverrideSeq := GVL_Hmi.stOverride.nSeq;
_nLaunchSeq := GVL_Hmi.stLaunch.nSeq;
_nOrderSeq := GVL_Hmi.stOrder.nSeq;
_nMoverVarSeq := GVL_Hmi.stMoverVar.nSeq;
_nPromptSeq := GVL_Hmi.stPrompt.nSeq;
_nPartSeq := GVL_Hmi.stPartCmd.nSeq;
_nAckSeq := GVL_Hmi.stAlarmAck.nSeq;
_nForceSeq := GVL_Hmi.stForceCmd.nSeq;
_nQualitySeq := GVL_Hmi.stQualityCmd.nSeq;
_nSimForceSeq := GVL_Hmi.stSimForce.nSeq;
_nLastHmiBeat := GVL_Hmi.nHmiHeartbeat;
_ApplyMoverForces();
_bInit := TRUE;
END_METHOD

// Simulation : chaque mover est posé sur une position tampon, sinon de parcage, sinon en ligne (docs/06 § 8)
METHOD PRIVATE _PlaceSimMovers
VAR
    i : USINT;
    k : INT;
    nPlaced : USINT;
    stPos : ST_Position;
END_VAR
IF NOT _bSimPlanar THEN RETURN; END_IF
fbPlanarSim.SetMoverCount(GVL_Active.stMachine.nMovers);
nPlaced := 0;
FOR k := 1 TO GVL_Active.stMachine.nPlanarPos DO
    IF nPlaced < GVL_Active.stMachine.nMovers AND (GVL_Active.stMachine.aPlanarPos[k].eKind = E_PlanarPosKind.Buffer
       OR GVL_Active.stMachine.aPlanarPos[k].eKind = E_PlanarPosKind.Park) THEN
        nPlaced := nPlaced + 1;
        fbPlanarSim.SetInitialPosition(nPlaced, GVL_Active.stMachine.aPlanarPos[k].stPos);
    END_IF
END_FOR
FOR i := nPlaced + 1 TO GVL_Active.stMachine.nMovers DO
    MEMSET(ADR(stPos), 0, SIZEOF(stPos));
    stPos.fX := USINT_TO_LREAL(i) * 120.0;
    fbPlanarSim.SetInitialPosition(i, stPos);
END_FOR
END_METHOD

// ================================================================== cycle

METHOD PRIVATE _Run
VAR
    n : USINT;
    k : UINT;
    eBefore : E_State;
    eCmd : E_Cmd;
    tNow : LINT;
END_VAR
IF NOT _bInit THEN _Init(); END_IF
tNow := F_NowUtc();
_nHeartbeat := _nHeartbeat + 1;
_WatchHmi();

// Chargeur : vérification, activation, restauration (machine Stopped)
fbLoader(bMachineStopped := fbPack.eState = E_State.Stopped);
fbLoader.Cycle();
IF fbLoader.bActivate THEN _Activate(); END_IF
IF fbLoader.bRestore THEN _ApplyRestore(); END_IF

// Demandes de la HMI
_HandleCmd();
_HandleOverride();
_HandleLaunch();
_HandleOrder();
_HandleMoverVar();
_HandlePrompt();
_HandlePart();
_HandleAlarmAck();
_HandleForce();
_HandleQuality();
_HandleSimForce();

// Arrêts automatiques demandés au cycle précédent : OF terminé, machine vidée, réaction qualité
IF fbSeq.bStopRequest AND _ePendingCmd = E_Cmd.None AND fbPack.Accepts(E_Cmd.Stop) THEN
    _ePendingCmd := E_Cmd.Stop;
    CASE fbSeq.nStopReason OF
        1: _eReasonNext := E_StopReason.OrderComplete;
        2: _eReasonNext := E_StopReason.Drained;
    ELSE
        _eReasonNext := E_StopReason.Quality;
    END_CASE
    _bOperatorAwaited := fbSeq.nStopReason <> 3;
END_IF

// Système planaire
UpdateOverride();
fbMgr.Cycle(EffectiveOverride);         // fait aussi tourner le pilote planaire
_PlanarAlarms();

// Machine d'états
eCmd := _ePendingCmd;
_ePendingCmd := E_Cmd.None;
eBefore := fbPack.eState;
fbPack(eCmd := eCmd, bStateComplete := _StateComplete(), bFault := _Fault());
IF fbPack.bEntry THEN _OnEntry(eBefore); END_IF

// Objets de niveau machine
_UpdateObjects();
fbManual.Cycle();

// Stations : forçages et réactions qualité, puis leur cycle
FOR n := 1 TO GVL_Const.MAX_STATIONS DO
    aStation[n].SetOutOfProduction(stForce.aStation[n].bOutOfProduction);
    aStation[n].SetQualityLock(fbQuality.aStationHold[n]);
    aStation[n].Cycle();
END_FOR

// Séquenceur général et calibration machine
fbSeq.Cycle(fbPack.eState);
IF fbCalib.bRunning AND (fbPack.eState <> E_State.Idle OR _eMode <> E_Mode.Maintenance) THEN fbCalib.Abort(); END_IF
fbCalib.Cycle(bHmiLost);

// Calibrations échues : une fois par jour (et à chaque Reset, voir _OnEntry)
IF tNow - _tLastCalibCheck > 864000000000 THEN _CheckCalibrations(tNow); END_IF

_Beacon();
fbQuality.Publish();
_fRetainedMs := _fRetainedMs + F_CycleMs();
IF _fRetainedMs >= 1000.0 THEN
    _fRetainedMs := 0;
    _PublishRetained();
END_IF
_Publish();
END_METHOD

METHOD PRIVATE _WatchHmi
IF GVL_Hmi.nHmiHeartbeat <> _nLastHmiBeat THEN
    _nLastHmiBeat := GVL_Hmi.nHmiHeartbeat;
    _fHmiQuietMs := 0;
    bHmiLost := FALSE;
ELSE
    _fHmiQuietMs := _fHmiQuietMs + F_CycleMs();
    IF _fHmiQuietMs >= GVL_Const.HMI_HEARTBEAT_MS AND NOT bHmiLost THEN
        bHmiLost := TRUE;
        fbAlarms.RaiseEvent(0, 917, 0, E_Severity.Info);           // la production continue (docs/06 § 6.2)
    END_IF
END_IF
END_METHOD

// ================================================================== machine d'états (docs/05 § 5.1 et 5.2)

METHOD PRIVATE _StateComplete : BOOL
VAR
    n : USINT;
    bAll : BOOL := TRUE;
END_VAR
_StateComplete := FALSE;
CASE fbPack.eState OF
    E_State.Resetting:
        // Système planaire actif, movers détectés (un mover hors service peut être absent), chaque fonction
        // utilisée a une station en Idle
        IF NOT _ipPlanar.Activate(TRUE) THEN RETURN; END_IF
        _StateComplete := _AllMoversDetected() AND fbSeq.MissingGroup(2) = 0;
    E_State.Stopping:
        FOR n := 1 TO GVL_Const.MAX_STATIONS DO
            IF aStation[n].State <> E_State.Stopped AND aStation[n].State <> E_State.Aborted THEN bAll := FALSE; END_IF
        END_FOR
        _StateComplete := bAll AND NOT fbMgr.AnyMoving();
    E_State.Clearing:
        FOR n := 1 TO GVL_Const.MAX_STATIONS DO
            IF aStation[n].State = E_State.Clearing THEN bAll := FALSE; END_IF
        END_FOR
        _StateComplete := bAll AND _ObjectsOk();
END_CASE
END_METHOD

METHOD PRIVATE _AllMoversDetected : BOOL
VAR
    i : USINT;
END_VAR
_AllMoversDetected := TRUE;
FOR i := 1 TO GVL_Active.stMachine.nMovers DO
    IF NOT _ipPlanar.MoverDetected(i) THEN
        IF NOT stForce.aMover[i].bOutOfService THEN
            _AllMoversDetected := FALSE;
            fbAlarms.Raise(_PlanarGlobalId(), 101, i, E_Severity.Warning);    // « Mover non détecté » : reste en Resetting
        END_IF
    ELSE
        fbAlarms.Clear(_PlanarGlobalId(), 101);
    END_IF
END_FOR
END_METHOD

METHOD PRIVATE _Fault : BOOL
VAR
    n : USINT;
    g : USINT;
    eSt : E_State;
    ipSafety : I_SafetyCircuits;
    k : UINT;
END_VAR
_Fault := FALSE;
eSt := fbPack.eState;
// Circuits de sécurité de niveau machine
FOR k := 1 TO _nObj DO
    IF aObj[k] <> 0 AND_THEN __QUERYINTERFACE(aObj[k], ipSafety) THEN
        IF NOT ipSafety.AllOk THEN _Fault := TRUE; END_IF
    END_IF
END_FOR
IF eSt = E_State.Stopped THEN RETURN; END_IF          // à l'arrêt, une sécurité ouverte n'est pas un défaut
// Système planaire, movers, alarmes de process de gravité défaut machine
IF _nPlanarError <> 0 OR fbSeq.bMachineFault THEN _Fault := TRUE; END_IF
IF eSt <> E_State.Aborted AND eSt <> E_State.Clearing THEN
    FOR n := 1 TO GVL_Const.MAX_STATIONS DO
        IF aStation[n].MachineFault THEN _Fault := TRUE; END_IF
    END_FOR
    IF NOT _ObjectsOk() THEN _Fault := TRUE; END_IF
END_IF
// Fonction utilisée sans station disponible (913) ; au Reset, fonction dont toutes les stations ont échoué
IF eSt = E_State.Execute THEN
    g := fbSeq.MissingGroup(1);
ELSIF eSt = E_State.Resetting THEN
    g := fbSeq.MissingGroup(3);
END_IF
IF g <> 0 THEN
    fbAlarms.RaiseEvent(0, 913, g, E_Severity.MachineFault);
    _Fault := TRUE;
END_IF
END_METHOD

METHOD PRIVATE _ObjectsOk : BOOL
VAR
    k : UINT;
END_VAR
_ObjectsOk := TRUE;
FOR k := 1 TO _nObj DO
    IF aObj[k] <> 0 THEN
        IF aObj[k].NodeState = E_ObjState.Error THEN _ObjectsOk := FALSE; END_IF
    END_IF
END_FOR
END_METHOD

METHOD PRIVATE _OnEntry
VAR_INPUT
    eFrom : E_State;
END_VAR
VAR
    n : USINT;
    k : UINT;
    eSt : E_State;
END_VAR
CASE fbPack.eState OF
    E_State.Stopped:
        fbSeq.bDrain := FALSE;
    E_State.Resetting:
        _ClearStopCause();
        FOR k := 1 TO _nObj DO IF aObj[k] <> 0 THEN aObj[k].Ack(); END_IF END_FOR
        _ipPlanar.AckError();
        FOR n := 1 TO GVL_Const.MAX_STATIONS DO
            IF _StationUsed(n) AND aStation[n].State = E_State.Stopped THEN aStation[n].Command(E_Cmd.Reset); END_IF
        END_FOR
        _CheckCalibrations(F_NowUtc());
    E_State.Idle:
        // Fin du Reset : chaque mover reçoit sa position, contextes et pièces gardés (docs/05 § 6.3)
        IF eFrom = E_State.Resetting THEN fbSeq.OnReset(); END_IF
    E_State.Execute:
        _ClearStopCause();
        _bOperatorAwaited := FALSE;
        FOR n := 1 TO GVL_Const.MAX_STATIONS DO
            eSt := aStation[n].State;
            IF _StationUsed(n) AND (eSt = E_State.Idle OR eSt = E_State.Held) THEN aStation[n].Command(E_Cmd.Start); END_IF
        END_FOR
    E_State.Held:
        _TakeStopCause();
        FOR n := 1 TO GVL_Const.MAX_STATIONS DO
            IF aStation[n].State = E_State.Execute THEN aStation[n].Command(E_Cmd.Hold); END_IF
        END_FOR
    E_State.Stopping:
        _TakeStopCause();
        fbSeq.bDrain := FALSE;
        FOR n := 1 TO GVL_Const.MAX_STATIONS DO
            eSt := aStation[n].State;
            IF eSt = E_State.Resetting OR eSt = E_State.Idle OR eSt = E_State.Execute OR eSt = E_State.Held THEN aStation[n].Command(E_Cmd.Stop); END_IF
        END_FOR
    E_State.Aborted:
        _TakeStopCause();
        fbSeq.bDrain := FALSE;
        fbMgr.StopAll();
        fbCalib.Abort();
        FOR k := _nObj TO 1 BY -1 DO IF aObj[k] <> 0 THEN aObj[k].Abort(); END_IF END_FOR
        FOR n := 1 TO GVL_Const.MAX_STATIONS DO
            IF aStation[n].State <> E_State.Aborted AND aStation[n].State <> E_State.Stopped THEN aStation[n].Command(E_Cmd.Abort); END_IF
        END_FOR
    E_State.Clearing:
        fbAlarms.AckFaults(0, FALSE, _sCmdUser);
        FOR k := 1 TO _nObj DO IF aObj[k] <> 0 THEN aObj[k].Ack(); END_IF END_FOR
        fbSeq.ClearFaults();
        _ipPlanar.AckError();
        FOR n := 1 TO GVL_Const.MAX_STATIONS DO
            IF aStation[n].State = E_State.Aborted THEN aStation[n].Command(E_Cmd.Clear); END_IF
        END_FOR
END_CASE
END_METHOD

METHOD PRIVATE _StationUsed : BOOL
VAR_INPUT
    n : USINT;
END_VAR
_StationUsed := GVL_Active.stMachine.aStation[n].bUsed;
END_METHOD

// Raison d'arrêt (docs/05 § 5.2, docs/14 § 4) : première alarme cause, sinon la cause système
METHOD PRIVATE _TakeStopCause
VAR
    i : INT;
END_VAR
IF _eStopReason <> E_StopReason.None THEN RETURN; END_IF      // déjà prise pour cet épisode d'arrêt
// Un arrêt automatique (OF terminé, machine vidée, qualité) garde sa cause même si une alarme est active
IF _eReasonNext = E_StopReason.OrderComplete OR _eReasonNext = E_StopReason.Drained OR _eReasonNext = E_StopReason.Quality THEN
    _eStopReason := _eReasonNext;
    _eReasonNext := E_StopReason.None;
    RETURN;
END_IF
i := fbAlarms.FirstFault(0, TRUE);
IF i > 0 THEN
    fbAlarms.MarkCause(i);
    _nStopCauseGid := GVL_Hmi.stAlarms.aActive[i].nGlobalId;
    _nStopCauseCode := GVL_Hmi.stAlarms.aActive[i].nCode;
    _eStopReason := E_StopReason.Alarm;
ELSE
    _eStopReason := E_StopReason.Operator;
END_IF
_eReasonNext := E_StopReason.None;
END_METHOD

METHOD PRIVATE _ClearStopCause
_nStopCauseGid := 0;
_nStopCauseCode := 0;
_eStopReason := E_StopReason.None;
_eReasonNext := E_StopReason.None;
END_METHOD

METHOD PRIVATE _CheckCalibrations
VAR_INPUT
    tNow : LINT;
END_VAR
VAR
    n : USINT;
END_VAR
_tLastCalibCheck := tNow;
FOR n := 1 TO GVL_Const.MAX_STATIONS DO
    IF _StationUsed(n) THEN aStation[n].CheckCalibrations(tNow); END_IF
END_FOR
fbCalib.CheckCalibrations(tNow);
END_METHOD

// ================================================================== système planaire et objets machine

METHOD PRIVATE _PlanarGlobalId : UDINT
VAR
    k : UINT;
END_VAR
_PlanarGlobalId := 0;
FOR k := 1 TO MIN(GVL_Active.stMachine.nMachineObj, GVL_Const.MAX_MACHINE_OBJ) DO
    IF GVL_Active.stMachine.aMachineObj[k].nType = GVL_Catalog.Type_PlanarSystem THEN
        _PlanarGlobalId := UINT_TO_UDINT(GVL_Active.stMachine.aMachineObj[k].nLocalId);
        RETURN;
    END_IF
END_FOR
END_METHOD

METHOD PRIVATE _PlanarAlarms
VAR
    nErr : UINT;
END_VAR
nErr := _ipPlanar.ErrorCode;
IF nErr <> _nPlanarError THEN
    IF _nPlanarError <> 0 THEN fbAlarms.Clear(_PlanarGlobalId(), _nPlanarError); END_IF
    IF nErr <> 0 THEN fbAlarms.Raise(_PlanarGlobalId(), nErr, _ipPlanar.ErrorSubCode, E_Severity.MachineFault); END_IF
    _nPlanarError := nErr;
END_IF
END_METHOD

METHOD PRIVATE _UpdateObjects
VAR
    k : UINT;
END_VAR
FOR k := 1 TO _nObj DO
    IF aObj[k] <> 0 THEN
        aObj[k].Cycle();
        GVL_Hmi.aObjStatus[0, k] := aObj[k].Status;
    END_IF
END_FOR
END_METHOD

// Balise de la machine (docs/02 § 8) : pilotée par l'état de la machine
METHOD PRIVATE _Beacon
VAR
    k : UINT;
    n : USINT;
    nPattern : UINT;
    bMaint : BOOL;
    bStationAborted : BOOL;
    ipBeacon : I_Beacon;
END_VAR
bMaint := _eMode = E_Mode.Maintenance OR stForce.eDeclared = E_DeclaredState.PlannedMaintenance
          OR stForce.eDeclared = E_DeclaredState.CorrectiveMaintenance OR stForce.eDeclared = E_DeclaredState.Trials
          OR stForce.eDeclared = E_DeclaredState.Cleaning;
FOR n := 1 TO GVL_Const.MAX_STATIONS DO
    IF aStation[n].State = E_State.Aborted THEN bStationAborted := TRUE; END_IF
END_FOR
nPattern := F_BeaconPattern(fbPack.eState, bMaint, _bOperatorAwaited OR fbSeq.AnyToEmpty() OR fbQuality.bAnyReaction, bStationAborted);
FOR k := 1 TO _nObj DO
    IF aObj[k] <> 0 AND_THEN __QUERYINTERFACE(aObj[k], ipBeacon) THEN ipBeacon.SetPattern(nPattern, (nPattern AND GVL_BeaconBits.BUZZER) <> 0); END_IF
END_FOR
END_METHOD

// ================================================================== déploiement (docs/05 § 10)

METHOD PRIVATE _Activate
VAR
    stResp : ST_DeployResponse;
    n : USINT;
    nOldGeneral : INT;
    nNewGeneral : INT;
    bRestart : BOOL;
END_VAR
IF fbPack.eState <> E_State.Stopped THEN
    F_AddDeployError(stResp, 21, 0, 0, 0, 0, 0);
    fbLoader.Finish(stResp);
    RETURN;
END_IF
nOldGeneral := _GeneralSize(GVL_Active.stMachine);
nNewGeneral := _GeneralSize(GVL_Hmi.stStaging.stMachine);
bRestart := NOT GVL_Active.bDeployed OR nOldGeneral <> nNewGeneral
            OR GVL_Active.stMachine.nMovers <> GVL_Hmi.stStaging.stMachine.nMovers
            OR GVL_Active.stMachine.nSlots <> GVL_Hmi.stStaging.stMachine.nSlots;
// Copie atomique de la zone d'attente (un seul cycle)
GVL_Active.stMachine := GVL_Hmi.stStaging.stMachine;
GVL_Active.aStation := GVL_Hmi.stStaging.aStation;
GVL_Active.aPressRecipe := GVL_Hmi.stStaging.aPressRecipe;
GVL_Active.stMachine.tDeployed := F_NowUtc();
GVL_Active.stMachine.sDeployedBy := fbLoader.Request.sUser;
GVL_Active.bDeployed := TRUE;
fbFactory.Build(stResp);
FOR n := 1 TO GVL_Const.MAX_STATIONS DO aStation[n].OnDeploy(); END_FOR
fbMgr.Configure();
fbSeq.OnDeploy(bRestart);
fbCalib.OnDeploy();
_PlaceSimMovers();
fbLoader.Finish(stResp);
END_METHOD

// Taille du process général : somme des longueurs de ses tables (docs/05 § 10, étape 4)
METHOD PRIVATE _GeneralSize : INT
VAR_IN_OUT
    stData : ST_MachineData;
END_VAR
VAR
    t : USINT;
END_VAR
_GeneralSize := 0;
FOR t := 1 TO GVL_Const.MAX_TABLES DO
    IF stData.aTable[t].bUsed AND (stData.aTable[t].eKind = E_TableKind.General OR stData.aTable[t].eKind = E_TableKind.GeneralSub) THEN
        _GeneralSize := _GeneralSize + stData.aTable[t].nLength;
    END_IF
END_FOR
END_METHOD

// Restauration des valeurs rémanentes (docs/05 § 10, étape 6)
METHOD PRIVATE _ApplyRestore
VAR
    k : UINT;
    nUnknown : UINT;
    stItem : ST_RetainedItem;
    bOk : BOOL;
END_VAR
FOR k := 1 TO fbLoader.nRestoreCount DO
    stItem := fbLoader.aRestore[k];
    bOk := FALSE;
    CASE stItem.eKind OF
        E_RetainedKind.StationVar:
            IF stItem.nStation >= 1 AND stItem.nStation <= GVL_Const.MAX_STATIONS THEN bOk := aStation[stItem.nStation].Restore(stItem); END_IF
        E_RetainedKind.Calibration:
            IF stItem.nStation = 0 THEN
                bOk := fbCalib.Restore(stItem);
            ELSIF stItem.nStation <= GVL_Const.MAX_STATIONS THEN
                bOk := aStation[stItem.nStation].Restore(stItem);
            END_IF
        E_RetainedKind.MoverVar:
            bOk := fbSeq.Restore(stItem);
        E_RetainedKind.ObjectCounters:
            bOk := fbFactory.Restore(stItem);
    END_CASE
    IF NOT bOk THEN nUnknown := nUnknown + 1; END_IF          // compteurs de production : avec FB_PlantPublisher
END_FOR
fbLoader.RestoreDone(nUnknown);
_PublishRetained();
END_METHOD

METHOD PRIVATE _PublishRetained
VAR
    n : USINT;
END_VAR
GVL_Hmi.stRetained.nRetainId := GVL_Active.nRetainId;
GVL_Hmi.stRetained.nCount := 0;
FOR n := 1 TO GVL_Const.MAX_STATIONS DO
    IF _StationUsed(n) THEN aStation[n].PublishRetained(GVL_Hmi.stRetained); END_IF
END_FOR
fbCalib.PublishRetained(GVL_Hmi.stRetained);
fbSeq.PublishRetained(GVL_Hmi.stRetained);
fbFactory.PublishRetained(GVL_Hmi.stRetained);
END_METHOD

// ================================================================== demandes de la HMI (docs/06 § 6.2)

METHOD PRIVATE _HandleCmd
VAR
    req : REFERENCE TO ST_CmdRequest;
    resp : REFERENCE TO ST_CmdResponse;
    n : USINT;
    eReason : E_RejectReason;
    eSt : E_State;
END_VAR
req REF= GVL_Hmi.stCmd;
resp REF= GVL_Hmi.stCmdResp;
IF req.nSeq = _nCmdSeq THEN RETURN; END_IF
_nCmdSeq := req.nSeq;
eReason := E_RejectReason.None;
IF req.nTarget = 0 THEN
    IF req.bSetMode THEN
        eReason := _SetMode(req.eMode);
    ELSE
        eReason := _MachineCommand(req.eCmd, req.sUser);
    END_IF
    resp.eState := fbPack.eState;
ELSIF req.nTarget > GVL_Const.MAX_STATIONS OR_ELSE NOT _StationUsed(req.nTarget) THEN
    eReason := E_RejectReason.NotConfigured;
ELSE
    // Commande d'une station seule : réintégration pendant que la machine produit (docs/05 § 5.1)
    n := req.nTarget;
    eSt := aStation[n].State;
    IF req.bSetMode THEN
        eReason := E_RejectReason.NotAllowed;       // le mode est celui de la machine
    ELSIF NOT (fbPack.eState = E_State.Execute OR fbPack.eState = E_State.Held) THEN
        eReason := E_RejectReason.WrongState;
    ELSIF (eSt = E_State.Execute OR eSt = E_State.Held) AND NOT aStation[n].OutOfProduction THEN
        eReason := E_RejectReason.NotAllowed;       // station en production : elle suit la machine
    ELSE
        eReason := aStation[n].Command(req.eCmd);
    END_IF
    resp.eState := eSt;
END_IF
resp.eReason := eReason;
resp.eResult := SEL(eReason = E_RejectReason.None, E_CmdResult.Rejected, E_CmdResult.Accepted);
resp.nDetail := 0;
resp.nSeq := req.nSeq;
END_METHOD

METHOD PRIVATE _MachineCommand : E_RejectReason
VAR_INPUT
    eCmd : E_Cmd;
    sUser : STRING(24);
END_VAR
VAR
    eSt : E_State;
END_VAR
eSt := fbPack.eState;
_MachineCommand := E_RejectReason.None;
CASE eCmd OF
    E_Cmd.DrainStart:
        IF eSt = E_State.Execute OR eSt = E_State.Held OR eSt = E_State.Idle THEN fbSeq.bDrain := TRUE;
        ELSE _MachineCommand := E_RejectReason.WrongState; END_IF
        RETURN;
    E_Cmd.DrainCancel:
        fbSeq.bDrain := FALSE;
        RETURN;
    E_Cmd.Start:
        IF stForce.bStartBlocked THEN _MachineCommand := E_RejectReason.StartBlocked; RETURN; END_IF
    E_Cmd.Reset:
        IF NOT GVL_Active.bDeployed THEN _MachineCommand := E_RejectReason.NotConfigured; RETURN; END_IF
        IF fbLoader.bBusy THEN _MachineCommand := E_RejectReason.Busy; RETURN; END_IF
END_CASE
IF eCmd = E_Cmd.None OR NOT fbPack.Accepts(eCmd) THEN
    _MachineCommand := E_RejectReason.WrongState;
    RETURN;
END_IF
_ePendingCmd := eCmd;
_sCmdUser := sUser;
IF eCmd = E_Cmd.Stop OR eCmd = E_Cmd.Hold OR eCmd = E_Cmd.Abort THEN _eReasonNext := E_StopReason.Operator; END_IF
END_METHOD

METHOD PRIVATE _SetMode : E_RejectReason
VAR_INPUT
    eMode : E_Mode;
END_VAR
VAR
    n : USINT;
END_VAR
IF fbPack.eState <> E_State.Stopped THEN _SetMode := E_RejectReason.WrongState; RETURN; END_IF
FOR n := 1 TO GVL_Const.MAX_STATIONS DO
    IF aStation[n].State <> E_State.Stopped THEN _SetMode := E_RejectReason.WrongState; RETURN; END_IF
END_FOR
_eMode := eMode;
FOR n := 1 TO GVL_Const.MAX_STATIONS DO aStation[n].SetMode(eMode); END_FOR
_SetMode := E_RejectReason.None;
END_METHOD

METHOD PRIVATE _HandleOverride
VAR
    req : REFERENCE TO ST_OverrideRequest;
    resp : REFERENCE TO ST_CmdResponse;
END_VAR
req REF= GVL_Hmi.stOverride;
resp REF= GVL_Hmi.stOverrideResp;
IF req.nSeq = _nOverrideSeq THEN RETURN; END_IF
_nOverrideSeq := req.nSeq;
resp.eReason := E_RejectReason.None;
IF req.nObject <> 0 OR req.bManual THEN
    resp.eReason := E_RejectReason.NotAllowed;          // override d'objet : propriété de la configuration (D94)
ELSIF req.nStation = 0 THEN
    _fMachineOverride := LIMIT(0.0, req.fValue, 100.0);
    SetOverride(_fMachineOverride);
ELSIF req.nStation <= GVL_Const.MAX_STATIONS THEN
    _aStationOverride[req.nStation] := LIMIT(0.0, req.fValue, 100.0);
    aStation[req.nStation].SetOverride(_aStationOverride[req.nStation]);
ELSE
    resp.eReason := E_RejectReason.NotConfigured;
END_IF
resp.eResult := SEL(resp.eReason = E_RejectReason.None, E_CmdResult.Rejected, E_CmdResult.Accepted);
resp.eState := fbPack.eState;
resp.nSeq := req.nSeq;
END_METHOD

METHOD PRIVATE _HandleLaunch
VAR
    req : REFERENCE TO ST_LaunchRequest;
    resp : REFERENCE TO ST_CmdResponse;
    pMover : POINTER TO ST_MoverCtx;
END_VAR
req REF= GVL_Hmi.stLaunch;
resp REF= GVL_Hmi.stLaunchResp;
IF req.nSeq = _nLaunchSeq THEN RETURN; END_IF
_nLaunchSeq := req.nSeq;
pMover := fbSeq.MoverPtr(req.nMover);
IF _eMode <> E_Mode.Maintenance THEN
    resp.eReason := E_RejectReason.WrongMode;
ELSIF req.nStation = 0 THEN
    // Calibration machine : machine Idle (docs/05 § 10.1)
    IF fbPack.eState <> E_State.Idle THEN resp.eReason := E_RejectReason.WrongState;
    ELSE resp.eReason := fbCalib.Start(req.nTable, req.nMover, pMover, req.sUser); END_IF
ELSIF req.nStation > GVL_Const.MAX_STATIONS OR_ELSE NOT _StationUsed(req.nStation) THEN
    resp.eReason := E_RejectReason.NotConfigured;
ELSE
    resp.eReason := aStation[req.nStation].Launch(req.nTable, req.nMover, pMover, req.nSlot, req.aParam, req.sUser);
END_IF
resp.eResult := SEL(resp.eReason = E_RejectReason.None, E_CmdResult.Rejected, E_CmdResult.Accepted);
resp.eState := fbPack.eState;
resp.nSeq := req.nSeq;
END_METHOD

METHOD PRIVATE _HandleOrder
VAR
    req : REFERENCE TO ST_OrderRequest;
    resp : REFERENCE TO ST_CmdResponse;
    eSt : E_State;
END_VAR
req REF= GVL_Hmi.stOrder;
resp REF= GVL_Hmi.stOrderResp;
IF req.nSeq = _nOrderSeq THEN RETURN; END_IF
_nOrderSeq := req.nSeq;
eSt := fbPack.eState;
CASE req.eCmd OF
    E_OrderCmd.Activate:
        IF eSt = E_State.Aborted OR eSt = E_State.Clearing OR eSt = E_State.Resetting OR eSt = E_State.Stopping THEN
            resp.eReason := E_RejectReason.WrongState;
        ELSE
            resp.eReason := fbSeq.Activate(req);
        END_IF
    E_OrderCmd.Suspend:
        resp.eReason := fbSeq.Suspend();
    E_OrderCmd.Cancel:
        resp.eReason := fbSeq.Cancel(req.nOrder);
ELSE
    resp.eReason := E_RejectReason.NotAllowed;
END_CASE
resp.eResult := SEL(resp.eReason = E_RejectReason.None, E_CmdResult.Rejected, E_CmdResult.Accepted);
resp.eState := eSt;
resp.nDetail := req.nOrder;
resp.nSeq := req.nSeq;
END_METHOD

METHOD PRIVATE _HandleMoverVar
VAR
    req : REFERENCE TO ST_MoverVarRequest;
    resp : REFERENCE TO ST_CmdResponse;
END_VAR
req REF= GVL_Hmi.stMoverVar;
resp REF= GVL_Hmi.stMoverVarResp;
IF req.nSeq = _nMoverVarSeq THEN RETURN; END_IF
_nMoverVarSeq := req.nSeq;
IF _eMode <> E_Mode.Maintenance THEN
    resp.eReason := E_RejectReason.WrongMode;
ELSIF fbPack.eState <> E_State.Stopped AND fbPack.eState <> E_State.Idle THEN
    resp.eReason := E_RejectReason.WrongState;
ELSE
    resp.eReason := fbSeq.SetMoverVar(req);
END_IF
resp.eResult := SEL(resp.eReason = E_RejectReason.None, E_CmdResult.Rejected, E_CmdResult.Accepted);
resp.eState := fbPack.eState;
resp.nSeq := req.nSeq;
END_METHOD

METHOD PRIVATE _HandlePrompt
VAR
    req : REFERENCE TO ST_PromptAnswer;
    resp : REFERENCE TO ST_CmdResponse;
    bOk : BOOL;
END_VAR
req REF= GVL_Hmi.stPrompt;
resp REF= GVL_Hmi.stPromptResp;
IF req.nSeq = _nPromptSeq THEN RETURN; END_IF
_nPromptSeq := req.nSeq;
IF req.nStation = 0 THEN
    bOk := fbCalib.AnswerPrompt(req.nPromptId, req.bConfirm);
ELSIF req.nStation <= GVL_Const.MAX_STATIONS THEN
    bOk := aStation[req.nStation].AnswerPrompt(req.nPromptId, req.bConfirm, req.fValue);
END_IF
resp.eReason := SEL(bOk, E_RejectReason.NotAllowed, E_RejectReason.None);
resp.eResult := SEL(bOk, E_CmdResult.Rejected, E_CmdResult.Accepted);
resp.eState := fbPack.eState;
resp.nSeq := req.nSeq;
END_METHOD

METHOD PRIVATE _HandlePart
VAR
    req : REFERENCE TO ST_PartRequest;
    resp : REFERENCE TO ST_CmdResponse;
END_VAR
req REF= GVL_Hmi.stPartCmd;
resp REF= GVL_Hmi.stPartResp;
IF req.nSeq = _nPartSeq THEN RETURN; END_IF
_nPartSeq := req.nSeq;
resp.eReason := fbSeq.ConfirmRemoved(req.nMover);
resp.eResult := SEL(resp.eReason = E_RejectReason.None, E_CmdResult.Rejected, E_CmdResult.Accepted);
resp.eState := fbPack.eState;
resp.nDetail := req.nMover;
resp.nSeq := req.nSeq;
END_METHOD

// Acquittement par l'opérateur : avertissements seulement ; les défauts s'acquittent par Clear (docs/14 § 4)
METHOD PRIVATE _HandleAlarmAck
VAR
    req : REFERENCE TO ST_AlarmAckRequest;
    resp : REFERENCE TO ST_CmdResponse;
END_VAR
req REF= GVL_Hmi.stAlarmAck;
resp REF= GVL_Hmi.stAlarmAckResp;
IF req.nSeq = _nAckSeq THEN RETURN; END_IF
_nAckSeq := req.nSeq;
fbAlarms.AckWarning(req.nGlobalId, req.nCode, req.sUser);
resp.eReason := E_RejectReason.None;
resp.eResult := E_CmdResult.Accepted;
resp.eState := fbPack.eState;
resp.nSeq := req.nSeq;
END_METHOD

// Forçages et état déclaré (docs/12) : mémorisés en persistant, publiés dans GVL_Hmi.stForce
METHOD PRIVATE _HandleForce
VAR
    req : REFERENCE TO ST_ForceCommand;
    resp : REFERENCE TO ST_CmdResponse;
    n : USINT;
END_VAR
req REF= GVL_Hmi.stForceCmd;
resp REF= GVL_Hmi.stForceResp;
IF req.nSeq = _nForceSeq THEN RETURN; END_IF
_nForceSeq := req.nSeq;
resp.eReason := E_RejectReason.None;
n := req.nTarget;
CASE req.eKind OF
    E_ForceKind.DeclaredState:
        IF req.bSet THEN
            stForce.eDeclared := req.eDeclared;
            stForce.bStartBlocked := req.bStartBlocked;
            stForce.nReason := req.nReason;
            stForce.tSince := F_NowUtc();
            stForce.tUntil := req.tUntil;
            stForce.sBy := req.sUser;
        ELSE
            stForce.eDeclared := E_DeclaredState.None;
            stForce.bStartBlocked := FALSE;
            stForce.nReason := 0;
            stForce.tSince := F_NowUtc();
            stForce.tUntil := 0;
            stForce.sBy := req.sUser;
        END_IF
    E_ForceKind.StationOutOfProduction:
        IF n < 1 OR n > GVL_Const.MAX_STATIONS OR_ELSE NOT _StationUsed(n) THEN
            resp.eReason := E_RejectReason.NotConfigured;
        ELSE
            stForce.aStation[n].bOutOfProduction := req.bSet;
            stForce.aStation[n].nReason := SEL(req.bSet, 0, req.nReason);
            stForce.aStation[n].tSince := F_NowUtc();
            stForce.aStation[n].sBy := req.sUser;
        END_IF
    E_ForceKind.MoverOutOfService:
        IF n < 1 OR n > GVL_Active.stMachine.nMovers THEN
            resp.eReason := E_RejectReason.NotConfigured;
        ELSE
            stForce.aMover[n].bOutOfService := req.bSet;
            stForce.aMover[n].bImmediate := req.bSet AND req.bImmediate;
            stForce.aMover[n].nReason := SEL(req.bSet, 0, req.nReason);
            stForce.aMover[n].tSince := F_NowUtc();
            stForce.aMover[n].sBy := req.sUser;
            fbSeq.SetOutOfService(n, req.bSet, req.bImmediate);
        END_IF
ELSE
    resp.eReason := E_RejectReason.NotAllowed;
END_CASE
resp.eResult := SEL(resp.eReason = E_RejectReason.None, E_CmdResult.Rejected, E_CmdResult.Accepted);
resp.eState := fbPack.eState;
resp.nSeq := req.nSeq;
END_METHOD

METHOD PRIVATE _ApplyMoverForces
VAR
    i : USINT;
END_VAR
FOR i := 1 TO GVL_Const.MAX_MOVERS DO
    fbSeq.SetOutOfService(i, stForce.aMover[i].bOutOfService, stForce.aMover[i].bImmediate);
END_FOR
END_METHOD

METHOD PRIVATE _HandleQuality
VAR
    req : REFERENCE TO ST_QualityCommand;
    resp : REFERENCE TO ST_CmdResponse;
END_VAR
req REF= GVL_Hmi.stQualityCmd;
resp REF= GVL_Hmi.stQualityResp;
IF req.nSeq = _nQualitySeq THEN RETURN; END_IF
_nQualitySeq := req.nSeq;
resp.eReason := E_RejectReason.None;
CASE req.eCmd OF
    E_QualityCmd.Release:
        IF NOT fbQuality.Release(req.eScope, req.nIndex) THEN resp.eReason := E_RejectReason.WrongState; END_IF
    E_QualityCmd.Reset:
        fbQuality.Reset(req.eScope, req.nIndex);
ELSE
    resp.eReason := E_RejectReason.NotAllowed;
END_CASE
resp.eResult := SEL(resp.eReason = E_RejectReason.None, E_CmdResult.Rejected, E_CmdResult.Accepted);
resp.eState := fbPack.eState;
resp.nSeq := req.nSeq;
END_METHOD

// Simulation : forcer une entrée simulée d'un objet (docs/12 § 5)
METHOD PRIVATE _HandleSimForce
VAR
    req : REFERENCE TO ST_SimForceRequest;
    resp : REFERENCE TO ST_CmdResponse;
    ipObj : I_Object;
END_VAR
req REF= GVL_Hmi.stSimForce;
resp REF= GVL_Hmi.stSimForceResp;
IF req.nSeq = _nSimForceSeq THEN RETURN; END_IF
_nSimForceSeq := req.nSeq;
IF req.nStation = 0 THEN
    ipObj := ObjectAt(req.nObject);
ELSIF req.nStation <= GVL_Const.MAX_STATIONS THEN
    ipObj := aStation[req.nStation].ObjectAt(req.nObject);
END_IF
IF ipObj = 0 THEN
    resp.eReason := E_RejectReason.NotConfigured;
ELSIF req.bRelease THEN
    ipObj.ForceInput(0, 0);
    resp.eReason := E_RejectReason.None;
ELSE
    ipObj.ForceInput(req.nSignal, req.fValue);
    resp.eReason := E_RejectReason.None;
END_IF
resp.eResult := SEL(resp.eReason = E_RejectReason.None, E_CmdResult.Rejected, E_CmdResult.Accepted);
resp.eState := fbPack.eState;
resp.nSeq := req.nSeq;
END_METHOD

// ================================================================== publication (docs/06 § 6.3)

METHOD PRIVATE _Publish
VAR
    s : REFERENCE TO ST_MachineStatus;
END_VAR
s REF= GVL_Hmi.stMachineStatus;
s.eState := fbPack.eState;
s.eMode := _eMode;
s.fEffOverride := LREAL_TO_REAL(EffectiveOverride);
s.nBeacon := F_BeaconPattern(fbPack.eState, _eMode = E_Mode.Maintenance, _bOperatorAwaited OR fbSeq.AnyToEmpty() OR fbQuality.bAnyReaction, FALSE);
s.bFault := fbPack.eState = E_State.Aborted;
s.tStateSince := fbPack.tSince;
s.nStopCauseGlobalId := _nStopCauseGid;
s.nStopCauseCode := _nStopCauseCode;
s.eStopReason := _eStopReason;
fbSeq.PublishOrders(s);
s.bDrain := fbSeq.bDrain;
s.nPartsInProcess := fbSeq.PartsInProcess();
s.bLoadingLocked := fbQuality.bLoadingLocked;
s.eDeclared := stForce.eDeclared;
s.bStartBlocked := stForce.bStartBlocked;
s.tPlcClock := F_NowUtc();
s.nPlcHeartbeat := _nHeartbeat;
s.sProjectHash := GVL_Active.stMachine.sProjectHash;
s.tDeployed := GVL_Active.stMachine.tDeployed;
s.bHmiLost := bHmiLost;
fbCalib.Publish(s);
GVL_Hmi.stForce := stForce;
END_METHOD

// ================================================================== I_ManualHost, I_ObjectHost

METHOD ObjectAt : I_Object
VAR_INPUT
    nLocalId : UINT;
END_VAR
IF nLocalId >= 1 AND nLocalId <= GVL_Const.MAX_MACHINE_OBJ THEN ObjectAt := aObj[nLocalId]; END_IF
END_METHOD

METHOD NamedPos : POINTER TO ST_NamedPos
VAR_INPUT
    nIndex : INT;
END_VAR
NamedPos := 0;                      // la machine n'a pas de positions nommées
END_METHOD

PROPERTY State : E_State
GET
State := fbPack.eState;
END_GET
END_PROPERTY

PROPERTY Mode : E_Mode
GET
Mode := _eMode;
END_GET
END_PROPERTY

METHOD ClearObjects
VAR
    k : UINT;
END_VAR
FOR k := 1 TO GVL_Const.MAX_MACHINE_OBJ DO aObj[k] := 0; END_FOR
_nObj := 0;
END_METHOD

METHOD SetObject
VAR_INPUT
    nLocalId : UINT;
    ipObj : I_Object;
END_VAR
IF nLocalId >= 1 AND nLocalId <= GVL_Const.MAX_MACHINE_OBJ THEN
    aObj[nLocalId] := ipObj;
    _nObj := MAX(_nObj, nLocalId);
END_IF
END_METHOD
END_FUNCTION_BLOCK
