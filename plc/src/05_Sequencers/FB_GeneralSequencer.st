// Séquenceur général (docs/05 § 5) : exécution du process général par mover, routage vers les stations des
// fonctions, files et priorités, préparation, fin d'opération (création et clôture des pièces, traces, qualité),
// table d'OF, fin des pièces en cours, variables de mover, mover à vider, mover hors service.
FUNCTION_BLOCK FB_GeneralSequencer
VAR
    _refMachine : REFERENCE TO ST_MachineData;
    _refMgr : REFERENCE TO FB_MoverManager;
    _refQuality : REFERENCE TO FB_QualityMonitor;
    _refAlarms : REFERENCE TO FB_AlarmManager;
    _refTraces : REFERENCE TO FB_TraceBuffer;
    aStation : ARRAY[1..GVL_Const.MAX_STATIONS] OF POINTER TO FB_Station;
    _aLastReceived : ARRAY[1..GVL_Const.MAX_STATIONS] OF LINT;
    _aNoStationMs : ARRAY[1..GVL_Const.MAX_MOVERS] OF LREAL;
    bDrain : BOOL;                  // fin des pièces en cours (docs/05 § 5.7)
    bStopRequest : BOOL;            // impulsion : arrêt automatique demandé à la machine
    nStopReason : UINT;             // 1 OF terminé, 2 machine vidée, 3 arrêt qualité
    bMachineFault : BOOL;           // mover en défaut, alarme de process « défaut machine »
    nFaultCode : UINT;
    _bExecute : BOOL;
    _bOrderDonePending : BOOL;      // l'OF actif vient de passer Done : arrêt quand plus aucune pièce
    _aGroupUsed : ARRAY[1..GVL_Const.MAX_GROUPS] OF BOOL;     // fonctions visées par une Route du process général
END_VAR
VAR PERSISTENT
    aMover : ARRAY[1..GVL_Const.MAX_MOVERS] OF ST_MoverCtx;
    aOrder : ARRAY[1..GVL_Const.MAX_ORDERS] OF ST_OrderState;
    nActiveOrder : USINT;           // rang dans aOrder, 0 = aucun
    _nReqSeq : UDINT;
    _nPartCounter : UDINT;
END_VAR

// ================================================================== mise en place

METHOD Init
VAR_INPUT
    refMachine : REFERENCE TO ST_MachineData;
    refMgr : REFERENCE TO FB_MoverManager;
    refQuality : REFERENCE TO FB_QualityMonitor;
    refAlarms : REFERENCE TO FB_AlarmManager;
    refTraces : REFERENCE TO FB_TraceBuffer;
END_VAR
_refMachine REF= refMachine;
_refMgr REF= refMgr;
_refQuality REF= refQuality;
_refAlarms REF= refAlarms;
_refTraces REF= refTraces;
END_METHOD

METHOD SetStation
VAR_INPUT
    nStation : USINT;
    pStation : POINTER TO FB_Station;
END_VAR
IF nStation >= 1 AND nStation <= GVL_Const.MAX_STATIONS THEN aStation[nStation] := pStation; END_IF
END_METHOD

// Reset machine (docs/05 § 6.3) : chaque mover détecté reçoit sa position ; les contextes et les pièces sont gardés
METHOD OnReset
VAR
    i : USINT;
    nPos : INT;
    ipDrv : I_PlanarDriver;
END_VAR
ipDrv := _refMgr.Driver;
FOR i := 1 TO _refMachine.nMovers DO
    aMover[i].bAbsent := FALSE;
    IF ipDrv <> 0 THEN
        IF NOT ipDrv.MoverDetected(i) THEN
            aMover[i].bAbsent := aMover[i].bOutOfService;       // D85 : un mover hors service peut être absent
            IF aMover[i].bAbsent AND __ISVALIDREF(_refAlarms) THEN _refAlarms.RaiseEvent(0, 915, i, E_Severity.Info); END_IF
        END_IF
    END_IF
    nPos := _refMgr.PositionAt(i);
    _refMgr.FreeAll(i, nPos);
    IF nPos > 0 THEN _refMgr.Reserve(nPos, i); END_IF
    aMover[i].nPos := nPos;
    CASE aMover[i].eState OF
        E_MoverState.Routing, E_MoverState.Halted, E_MoverState.Unknown, E_MoverState.Error:
            aMover[i].eState := E_MoverState.Ready;             // la Route en cours est reprise au Start
        E_MoverState.Queued:
            aMover[i].eState := E_MoverState.Buffered;
        E_MoverState.AtStation:
            IF NOT aMover[i].bStationAborted THEN
                aMover[i].eState := E_MoverState.Ready;
                aMover[i].bRequestPosted := FALSE;
            END_IF
    END_CASE
    IF aMover[i].nTable = 0 THEN
        aMover[i].nTable := 1;
        aMover[i].nPC := _refMachine.aTable[1].nStart;
    END_IF
END_FOR
END_METHOD

// Nouveau déploiement (docs/05 § 10) : variables de mover reprises par identifiant et type ; movers remis à
// l'étape 1 si le process général a changé de taille
METHOD OnDeploy
VAR_INPUT
    bRestartMovers : BOOL;
END_VAR
VAR
    i : USINT;
    v : USINT;
    k : USINT;
    stOld : ST_MoverCtx;
    bFound : BOOL;
END_VAR
FOR i := 1 TO GVL_Const.MAX_MOVERS DO
    stOld := aMover[i];
    FOR v := 1 TO GVL_Const.MAX_MOVER_VARS DO
        aMover[i].aVar[v] := 0;
        MEMSET(ADR(aMover[i].aVarPos[v]), 0, SIZEOF(ST_Position));
        aMover[i].aVarId[v] := 0;
        IF v <= _refMachine.nMoverVar THEN
            aMover[i].aVarId[v] := _refMachine.aMoverVar[v].nId;
            aMover[i].aVar[v] := _refMachine.aMoverVar[v].fInit;
            aMover[i].aVarPos[v] := _refMachine.aMoverVar[v].stInitPos;
            bFound := FALSE;
            IF _refMachine.aMoverVar[v].eReset <> E_MoverVarReset.OnDeploy THEN
                FOR k := 1 TO GVL_Const.MAX_MOVER_VARS DO
                    IF NOT bFound AND stOld.aVarId[k] = _refMachine.aMoverVar[v].nId AND stOld.aVarId[k] <> 0 THEN
                        aMover[i].aVar[v] := stOld.aVar[k];
                        aMover[i].aVarPos[v] := stOld.aVarPos[k];
                        bFound := TRUE;
                    END_IF
                END_FOR
            END_IF
        END_IF
    END_FOR
    IF bRestartMovers OR i > _refMachine.nMovers THEN
        aMover[i].nTable := 1;
        aMover[i].nPC := _refMachine.aTable[1].nStart;
        aMover[i].nDepth := 0;
        aMover[i].eState := E_MoverState.Ready;
        aMover[i].nTargetGroup := 0;
        aMover[i].bRequestPosted := FALSE;
    END_IF
END_FOR
IF bRestartMovers AND __ISVALIDREF(_refAlarms) THEN _refAlarms.RaiseEvent(0, 916, _refMachine.nMovers, E_Severity.Info); END_IF
ScanGroups();
END_METHOD

// ================================================================== cycle

METHOD Cycle
VAR_INPUT
    eState : E_State;               // état de la machine
END_VAR
VAR
    n : USINT;
    i : USINT;
    nArr : INT;
    nOldest : USINT;
END_VAR
_bExecute := eState = E_State.Execute;
bStopRequest := FALSE;
// Ordre des n°, en commençant par le mover qui attend depuis le plus longtemps (pas de famine)
nOldest := 1;
FOR i := 1 TO _refMachine.nMovers DO
    IF (aMover[i].eState = E_MoverState.Buffered OR aMover[i].eState = E_MoverState.Queued)
       AND (NOT (aMover[nOldest].eState = E_MoverState.Buffered OR aMover[nOldest].eState = E_MoverState.Queued) OR aMover[i].tWaitStart < aMover[nOldest].tWaitStart) THEN
        nOldest := i;
    END_IF
END_FOR
FOR n := 1 TO _refMachine.nMovers DO
    i := ((nOldest - 1 + n - 1) MOD _refMachine.nMovers) + 1;
    // Arrivées et timeouts, dans tous les états
    nArr := _refMgr.Arrived(i);
    IF nArr > 0 THEN _Arrive(i, nArr); END_IF
    IF _refMgr.TimedOut(i) AND aMover[i].eState <> E_MoverState.Error THEN
        aMover[i].eState := E_MoverState.Error;
        bMachineFault := TRUE;
        nFaultCode := 914;
        IF __ISVALIDREF(_refAlarms) THEN _refAlarms.RaiseEvent(0, 914, i, E_Severity.MachineFault); END_IF
    END_IF
    // Réaction qualité « mover hors service » (docs/10 § 5.1)
    aMover[i].bQualityLock := _refQuality.aMoverOut[i];

    IF _bExecute THEN
        IF (aMover[i].bOutOfService OR aMover[i].bQualityLock) AND aMover[i].eState = E_MoverState.Ready AND _Empty(i) THEN
            _SendToPark(i, FALSE);
        ELSIF aMover[i].bOutOfService AND aMover[i].bImmediate AND aMover[i].eState = E_MoverState.Ready AND NOT _Empty(i) THEN
            _SendToPark(i, TRUE);
        END_IF
        CASE aMover[i].eState OF
            E_MoverState.Ready: _RunMover(i);
            E_MoverState.Buffered:
                IF aMover[i].nTargetGroup = 0 THEN aMover[i].eState := E_MoverState.Ready; ELSE _TryRoute(i); END_IF
            E_MoverState.Queued: _Queued(i);
            E_MoverState.AtStation: _AtStation(i);
            E_MoverState.Halted: _Resume(i);
            E_MoverState.OutOfService:
                IF NOT aMover[i].bOutOfService AND NOT aMover[i].bQualityLock THEN _Restart(i); END_IF
        END_CASE
    ELSE
        // Held, Stopping : plus de nouveau routage ; les opérations lancées se terminent (docs/05 § 5.3)
        IF aMover[i].eState = E_MoverState.AtStation THEN _AtStation(i);
        ELSIF aMover[i].eState = E_MoverState.Ready OR aMover[i].eState = E_MoverState.Buffered OR aMover[i].eState = E_MoverState.Queued THEN
            IF eState = E_State.Held OR eState = E_State.Stopping THEN aMover[i].eWait := E_WaitReason.Held; END_IF
        END_IF
    END_IF
END_FOR
_CheckOrders();
IF bDrain AND (_bExecute OR eState = E_State.Held) AND PartsInProcess() = 0 AND NOT EntryInProgress() THEN
    bStopRequest := TRUE;
    nStopReason := 2;                                   // machine vidée (docs/05 § 5.7)
END_IF
IF _refQuality.bMachineStop THEN
    bStopRequest := TRUE;
    nStopReason := 3;
END_IF
_Publish();
END_METHOD

// ================================================================== process général d'un mover (docs/05 § 5.3)

METHOD PRIVATE _RunMover
VAR_INPUT
    i : USINT;
END_VAR
VAR
    m : REFERENCE TO ST_MoverCtx;
    ins : REFERENCE TO ST_Instr;
    nGuard : INT;
    n : INT;
    eSev : E_Severity;
END_VAR
m REF= aMover[i];
FOR nGuard := 1 TO 64 DO
    IF m.nPC < 1 OR m.nPC > _refMachine.nInstrCount THEN
        m.nTable := 1;
        m.nPC := _refMachine.aTable[1].nStart;
        RETURN;
    END_IF
    ins REF= _refMachine.aInstr[m.nPC];
    CASE ins.eOp OF
        E_Op.Jump:
            m.nPC := DINT_TO_INT(ins.aArg[1]);
        E_Op.JumpIfFalse:
            _MarkVerdictTested(i, DINT_TO_INT(ins.aArg[1]));
            IF _EvalCond(i, DINT_TO_INT(ins.aArg[1])) THEN m.nPC := m.nPC + 1; ELSE m.nPC := DINT_TO_INT(ins.aArg[2]); END_IF
        E_Op.SetVar:
            _SetVar(i);
            m.nPC := m.nPC + 1;
        E_Op.ResetMoverVars:
            _ResetVars(i, DINT_TO_INT(ins.aArg[1]));
            m.nPC := m.nPC + 1;
        E_Op.LoopInit:
            n := LIMIT(1, DINT_TO_INT(ins.aArg[1]), 8);
            m.aHidden[n] := _Resolve(i, ins.aParam[1]);
            m.nPC := m.nPC + 1;
        E_Op.LoopNext:
            n := LIMIT(1, DINT_TO_INT(ins.aArg[1]), 8);
            m.aHidden[n] := m.aHidden[n] - 1;
            IF m.aHidden[n] > 0 THEN m.nPC := DINT_TO_INT(ins.aArg[2]); ELSE m.nPC := m.nPC + 1; END_IF
        E_Op.Call:
            IF m.nDepth >= GVL_Const.MAX_CALL_DEPTH THEN
                m.eState := E_MoverState.Error;
                bMachineFault := TRUE;
                nFaultCode := 904;
                IF __ISVALIDREF(_refAlarms) THEN _refAlarms.RaiseEvent(0, 904, i, E_Severity.MachineFault); END_IF
                RETURN;
            END_IF
            m.nDepth := m.nDepth + 1;
            m.aRetTable[m.nDepth] := m.nTable;
            m.aRetPC[m.nDepth] := m.nPC + 1;
            m.nTable := DINT_TO_INT(ins.aArg[1]);
            m.nPC := _refMachine.aTable[m.nTable].nStart;
        E_Op.Return:
            IF m.nDepth = 0 THEN
                m.nTable := 1;
                m.nPC := _refMachine.aTable[1].nStart;
                RETURN;
            END_IF
            m.nTable := m.aRetTable[m.nDepth];
            m.nPC := m.aRetPC[m.nDepth];
            m.nDepth := m.nDepth - 1;
        E_Op.Alarm:
            // aArg[2] : 1 avertissement, sinon défaut machine (pas de station au niveau machine)
            IF ins.aArg[2] = 1 THEN eSev := E_Severity.Warning; ELSE eSev := E_Severity.MachineFault; END_IF
            IF __ISVALIDREF(_refAlarms) THEN _refAlarms.RaiseEvent(0, DINT_TO_UINT(ins.aArg[3]), i, eSev); END_IF
            IF ins.aArg[2] >= 2 THEN
                m.eState := E_MoverState.Error;
                bMachineFault := TRUE;
                nFaultCode := DINT_TO_UINT(ins.aArg[3]);
                RETURN;
            END_IF
            m.nPC := m.nPC + 1;
        E_Op.WaitCond:
            IF NOT _EvalCond(i, DINT_TO_INT(ins.aArg[1])) THEN RETURN; END_IF
            m.nPC := m.nPC + 1;
        E_Op.End:
            IF NOT _Empty(i) AND __ISVALIDREF(_refAlarms) THEN _refAlarms.RaiseEvent(0, 910, i, E_Severity.Warning); END_IF
            m.nTable := 1;
            m.nDepth := 0;
            m.nPC := _refMachine.aTable[1].nStart;
            RETURN;                                     // un tour de process par cycle au plus
        E_Op.Route:
            _StartRoute(i);
            RETURN;
    ELSE
        m.nPC := m.nPC + 1;                             // Nop, StepBegin, StepEnd
    END_CASE
END_FOR
END_METHOD

// ================================================================== routage (docs/05 § 5.4)

METHOD PRIVATE _StartRoute
VAR_INPUT
    i : USINT;
END_VAR
VAR
    m : REFERENCE TO ST_MoverCtx;
    ins : REFERENCE TO ST_Instr;
    nGroup : USINT;
    k : USINT;
    s : USINT;
    bBad : BOOL;
    bHandled : BOOL;
END_VAR
m REF= aMover[i];
ins REF= _refMachine.aInstr[m.nPC];
nGroup := DINT_TO_USINT(ins.aArg[1]);
m.nTargetOp := DINT_TO_USINT(ins.aArg[2]);
m.nTargetSlot := DINT_TO_USINT(ins.aArg[3]);
// 1. Pièce mauvaise non traitée par le process : redirection vers la fonction de rebut
FOR s := 1 TO GVL_Const.MAX_SLOTS DO
    IF m.aSlot[s].bPresent THEN
        bBad := bBad OR m.aSlot[s].aData[1] = 2;
        bHandled := bHandled OR m.aSlot[s].bBadHandled;
    END_IF
END_FOR
IF _refMachine.nRejectGroup > 0 AND nGroup <> _refMachine.nRejectGroup AND bBad AND NOT bHandled
   AND nGroup >= 1 AND nGroup <= GVL_Const.MAX_GROUPS AND_THEN _refMachine.aGroup[nGroup].eRole <> E_GroupRole.Reject THEN
    nGroup := _refMachine.nRejectGroup;
    m.nTargetOp := 1;
    MEMSET(ADR(m.aOpParam), 0, SIZEOF(m.aOpParam));
ELSE
    FOR k := 1 TO GVL_Const.MAX_PARAMS DO m.aOpParam[k] := _Resolve(i, ins.aParam[k]); END_FOR
END_IF
m.nTargetGroup := nGroup;
m.nStation := 0;
_aNoStationMs[i] := 0;
_TryRoute(i);
END_METHOD

METHOD PRIVATE _TryRoute
VAR_INPUT
    i : USINT;
END_VAR
VAR
    m : REFERENCE TO ST_MoverCtx;
    g : REFERENCE TO ST_GroupCfg;
    s : USINT;
    nBest : USINT;
    nLoad : INT;
    nBestLoad : INT;
    nDest : INT;
    k : USINT;
    eWait : E_WaitReason;
END_VAR
m REF= aMover[i];
IF m.nTargetGroup < 1 OR m.nTargetGroup > GVL_Const.MAX_GROUPS THEN m.eState := E_MoverState.Ready; RETURN; END_IF
g REF= _refMachine.aGroup[m.nTargetGroup];
// Fonction d'entrée : pas de chargement sans OF actif, OF complet, arrêt qualité du chargement, fin des pièces
IF g.eRole = E_GroupRole.Entry THEN
    eWait := _LoadingLock();
    IF eWait <> E_WaitReason.None THEN _WaitInBuffer(i, eWait); RETURN; END_IF
END_IF
// Candidates : stations membres en Execute, sans défaut, sans arrêt qualité, en production
nBest := 0;
IF NOT m.bOutOfService AND NOT m.bQualityLock THEN
    FOR s := 1 TO GVL_Const.MAX_STATIONS DO
        IF (SHR(g.nStationMask, s - 1) AND 1) = 1 AND_THEN _Routable(s) THEN
            nLoad := _LoadOf(s);
            IF nBest = 0 OR nLoad < nBestLoad OR (nLoad = nBestLoad AND _aLastReceived[s] < _aLastReceived[nBest]) THEN
                nBest := s;
                nBestLoad := nLoad;
            END_IF
        END_IF
    END_FOR
END_IF
IF nBest = 0 THEN
    _aNoStationMs[i] := _aNoStationMs[i] + F_CycleMs();
    IF (g.eRole = E_GroupRole.Reject OR _refMachine.nRejectGroup = 0) AND _aNoStationMs[i] > UDINT_TO_LREAL(_refMachine.nNoStationMs) AND NOT _Empty(i) THEN
        _SendToPark(i, TRUE);                       // mover à vider (911)
        RETURN;
    END_IF
    _WaitInBuffer(i, E_WaitReason.NoStation);
    RETURN;
END_IF
_aNoStationMs[i] := 0;
// Destination : position de travail libre et sans file, sinon une place de file, sinon un tampon
nDest := 0;
// (une position déjà tenue par ce mover compte comme libre : reprise après Reset)
IF _refMachine.aStation[nBest].nWorkPos > 0 AND_THEN _FreeFor(_refMachine.aStation[nBest].nWorkPos, i) AND NOT _HasQueue(nBest) THEN
    nDest := _refMachine.aStation[nBest].nWorkPos;
ELSE
    FOR k := 1 TO GVL_Const.MAX_QUEUE DO
        IF nDest = 0 AND _refMachine.aStation[nBest].aQueuePos[k] > 0 AND_THEN _FreeFor(_refMachine.aStation[nBest].aQueuePos[k], i) THEN
            nDest := _refMachine.aStation[nBest].aQueuePos[k];
        END_IF
    END_FOR
END_IF
IF nDest = 0 THEN
    _WaitInBuffer(i, E_WaitReason.NoStation);
    RETURN;
END_IF
_aLastReceived[nBest] := F_NowUtc();
m.nStation := nBest;
m.eWait := E_WaitReason.None;
IF _refMgr.MoveTo(i, nDest) THEN
    m.nDest := nDest;
    m.nPos := 0;
    m.eState := E_MoverState.Routing;
    m.bRequestPosted := FALSE;
    // Préparation : la demande est posée dès la réservation de la position de travail (docs/05 § 4.1)
    IF nDest = _refMachine.aStation[nBest].nWorkPos THEN _PostIfPrepare(i); END_IF
END_IF
END_METHOD

METHOD PRIVATE _FreeFor : BOOL
VAR_INPUT
    nPos : INT;
    i : USINT;
END_VAR
_FreeFor := _refMgr.Owner(nPos) = 0 OR _refMgr.Owner(nPos) = i;
END_METHOD

METHOD PRIVATE _LoadingLock : E_WaitReason
_LoadingLock := E_WaitReason.None;
IF _refQuality.bLoadingLocked THEN _LoadingLock := E_WaitReason.QualityStop;
ELSIF bDrain THEN _LoadingLock := E_WaitReason.Drain;
ELSIF nActiveOrder = 0 THEN _LoadingLock := E_WaitReason.NoOrder;
ELSIF aOrder[nActiveOrder].nGood + aOrder[nActiveOrder].nInProcess >= aOrder[nActiveOrder].nQuantity THEN _LoadingLock := E_WaitReason.OrderComplete;
END_IF
END_METHOD

METHOD PRIVATE _Routable : BOOL
VAR_INPUT
    s : USINT;
END_VAR
IF aStation[s] = 0 THEN _Routable := FALSE; RETURN; END_IF
_Routable := aStation[s]^.State = E_State.Execute AND NOT aStation[s]^.OutOfProduction AND NOT aStation[s]^.QualityLock
             AND _refMachine.aStation[s].bUsed;
END_METHOD

METHOD PRIVATE _LoadOf : INT
VAR_INPUT
    s : USINT;
END_VAR
VAR
    i : USINT;
END_VAR
_LoadOf := 0;
FOR i := 1 TO _refMachine.nMovers DO
    IF aMover[i].nStation = s THEN _LoadOf := _LoadOf + 1; END_IF
END_FOR
END_METHOD

METHOD PRIVATE _HasQueue : BOOL
VAR_INPUT
    s : USINT;
END_VAR
VAR
    i : USINT;
END_VAR
_HasQueue := FALSE;
FOR i := 1 TO _refMachine.nMovers DO
    IF aMover[i].nStation = s AND aMover[i].eState = E_MoverState.Queued THEN _HasQueue := TRUE; END_IF
END_FOR
END_METHOD

METHOD PRIVATE _WaitInBuffer
VAR_INPUT
    i : USINT;
    eReason : E_WaitReason;
END_VAR
VAR
    m : REFERENCE TO ST_MoverCtx;
    nBuf : INT;
END_VAR
m REF= aMover[i];
IF m.eState <> E_MoverState.Buffered THEN m.tWaitStart := F_NowUtc(); END_IF
m.eWait := eReason;
m.nStation := 0;
// Déjà sur un tampon ou au parcage : il y reste
IF m.nPos > 0 AND_THEN (_refMachine.aPlanarPos[m.nPos].eKind = E_PlanarPosKind.Buffer OR _refMachine.aPlanarPos[m.nPos].eKind = E_PlanarPosKind.Park) THEN
    m.eState := E_MoverState.Buffered;
    RETURN;
END_IF
IF m.eState = E_MoverState.Routing THEN RETURN; END_IF
nBuf := _refMgr.NearestFree(i, E_PlanarPosKind.Buffer);
IF nBuf > 0 AND_THEN _refMgr.MoveTo(i, nBuf) THEN
    m.nDest := nBuf;
    m.nPos := 0;
    m.eState := E_MoverState.Routing;
ELSE
    m.eState := E_MoverState.Buffered;                      // aucun tampon libre : il reste où il est
END_IF
END_METHOD

METHOD PRIVATE _SendToPark
VAR_INPUT
    i : USINT;
    bToEmpty : BOOL;
END_VAR
VAR
    m : REFERENCE TO ST_MoverCtx;
    nPark : INT;
END_VAR
m REF= aMover[i];
m.nStation := 0;
m.bToEmptyPark := bToEmpty;
IF bToEmpty AND __ISVALIDREF(_refAlarms) THEN _refAlarms.Raise(0, 911, i, E_Severity.Warning); END_IF
nPark := _refMachine.nParkPos;
IF nPark = 0 OR m.nPos = nPark THEN
    m.eState := SEL(bToEmpty OR NOT _Empty(i), E_MoverState.OutOfService, E_MoverState.ToEmpty);
    RETURN;
END_IF
IF _refMgr.MoveTo(i, nPark) THEN
    m.nDest := nPark;
    m.nPos := 0;
    m.eState := E_MoverState.Routing;
END_IF
END_METHOD

METHOD PRIVATE _Arrive
VAR_INPUT
    i : USINT;
    nPos : INT;
END_VAR
VAR
    m : REFERENCE TO ST_MoverCtx;
END_VAR
m REF= aMover[i];
m.nPos := nPos;
m.nDest := 0;
CASE _refMachine.aPlanarPos[nPos].eKind OF
    E_PlanarPosKind.Work:
        m.eState := E_MoverState.AtStation;
        IF m.bRequestPosted AND m.nStation >= 1 AND_THEN aStation[m.nStation] <> 0 THEN aStation[m.nStation]^.MoverArrived(m.nReqId); END_IF
    E_PlanarPosKind.Queue:
        // En Held et Stopping, le mover arrivé passe Halted (docs/05 § 5.3)
        IF _bExecute THEN m.eState := E_MoverState.Queued; ELSE m.eState := E_MoverState.Halted; END_IF
    E_PlanarPosKind.Buffer:
        IF _bExecute THEN m.eState := E_MoverState.Buffered; ELSE m.eState := E_MoverState.Halted; END_IF
    E_PlanarPosKind.Park:
        IF m.bToEmptyPark OR NOT _Empty(i) THEN m.eState := E_MoverState.ToEmpty; ELSE m.eState := E_MoverState.OutOfService; END_IF
END_CASE
m.tWaitStart := F_NowUtc();
END_METHOD

// Retour en Execute d'un mover Halted : il reprend selon la position où il se trouve, sans perdre son PC
METHOD PRIVATE _Resume
VAR_INPUT
    i : USINT;
END_VAR
VAR
    m : REFERENCE TO ST_MoverCtx;
END_VAR
m REF= aMover[i];
m.eWait := E_WaitReason.None;
IF m.nPos < 1 OR m.nPos > _refMachine.nPlanarPos OR m.nTargetGroup = 0 THEN
    m.eState := E_MoverState.Ready;
    RETURN;
END_IF
CASE _refMachine.aPlanarPos[m.nPos].eKind OF
    E_PlanarPosKind.Queue:
        IF m.nStation > 0 THEN m.eState := E_MoverState.Queued; ELSE m.eState := E_MoverState.Buffered; END_IF
    E_PlanarPosKind.Work:
        m.eState := E_MoverState.AtStation;
ELSE
    m.eState := E_MoverState.Buffered;
END_CASE
END_METHOD

// File : servie par priorité de la fonction visée (la plus haute d'abord), puis ordre d'arrivée
METHOD PRIVATE _Queued
VAR_INPUT
    i : USINT;
END_VAR
VAR
    m : REFERENCE TO ST_MoverCtx;
    s : USINT;
    j : USINT;
    nFirst : USINT;
    nWork : INT;
END_VAR
m REF= aMover[i];
s := m.nStation;
IF s < 1 OR s > GVL_Const.MAX_STATIONS OR_ELSE NOT _Routable(s) THEN
    m.eState := E_MoverState.Ready;                 // la station n'est plus disponible : le mover est réaffecté
    m.nStation := 0;
    RETURN;
END_IF
nWork := _refMachine.aStation[s].nWorkPos;
IF _refMgr.Owner(nWork) <> 0 THEN RETURN; END_IF
nFirst := 0;
FOR j := 1 TO _refMachine.nMovers DO
    IF aMover[j].eState = E_MoverState.Queued AND aMover[j].nStation = s THEN
        IF nFirst = 0 THEN
            nFirst := j;
        ELSIF _refMachine.aGroup[aMover[j].nTargetGroup].nPriority > _refMachine.aGroup[aMover[nFirst].nTargetGroup].nPriority
           OR (_refMachine.aGroup[aMover[j].nTargetGroup].nPriority = _refMachine.aGroup[aMover[nFirst].nTargetGroup].nPriority
               AND aMover[j].tWaitStart < aMover[nFirst].tWaitStart) THEN
            nFirst := j;
        END_IF
    END_IF
END_FOR
IF nFirst <> i THEN RETURN; END_IF
IF _refMgr.MoveTo(i, nWork) THEN
    m.nDest := nWork;
    m.nPos := 0;
    m.eState := E_MoverState.Routing;
    m.bRequestPosted := FALSE;
    _PostIfPrepare(i);
END_IF
END_METHOD

// ================================================================== demande d'opération

METHOD PRIVATE _PostIfPrepare
VAR_INPUT
    i : USINT;
END_VAR
VAR
    s : USINT;
END_VAR
s := aMover[i].nStation;
IF s < 1 OR s > GVL_Const.MAX_STATIONS OR_ELSE aStation[s] = 0 THEN RETURN; END_IF
IF aStation[s]^.PrepareWanted(aMover[i].nTargetGroup, aMover[i].nTargetOp) THEN _PostRequest(i, FALSE); END_IF
END_METHOD

METHOD PRIVATE _PostRequest : BOOL
VAR_INPUT
    i : USINT;
    bAtStation : BOOL;
END_VAR
VAR
    m : REFERENCE TO ST_MoverCtx;
    stReq : ST_OpRequest;
    s : USINT;
    k : USINT;
END_VAR
m REF= aMover[i];
s := m.nStation;
_PostRequest := FALSE;
IF s < 1 OR s > GVL_Const.MAX_STATIONS OR_ELSE aStation[s] = 0 THEN RETURN; END_IF
IF aStation[s]^.Response.eStatus <> E_OpStatus.Idle THEN RETURN; END_IF
_nReqSeq := _nReqSeq + 1;
stReq.nReqId := _nReqSeq;
stReq.nGroup := m.nTargetGroup;
stReq.nOperation := m.nTargetOp;
stReq.nMover := i;
stReq.nSlot := m.nTargetSlot;
stReq.bMoverAtStation := bAtStation;
stReq.aParam := m.aOpParam;
IF aStation[s]^.Offer(stReq, ADR(aMover[i])) THEN
    m.nReqId := stReq.nReqId;
    m.bRequestPosted := TRUE;
    m.tOpStart := F_NowUtc();
    FOR k := 1 TO GVL_Const.MAX_SLOTS DO
        m.aBadAtArrival[k] := m.aSlot[k].bPresent AND m.aSlot[k].aData[1] = 2;
    END_FOR
    _PostRequest := TRUE;
END_IF
END_METHOD

METHOD PRIVATE _AtStation
VAR_INPUT
    i : USINT;
END_VAR
VAR
    m : REFERENCE TO ST_MoverCtx;
    s : USINT;
    k : USINT;
    eSt : E_State;
END_VAR
m REF= aMover[i];
s := m.nStation;
IF s < 1 OR s > GVL_Const.MAX_STATIONS OR_ELSE aStation[s] = 0 THEN m.eState := E_MoverState.Ready; RETURN; END_IF
eSt := aStation[s]^.State;
IF m.bStationAborted THEN
    // Le mover reste en position de travail jusqu'à la fin du Clearing, puis part au rebut (docs/05 § 6.4)
    IF eSt = E_State.Stopped OR eSt = E_State.Idle OR eSt = E_State.Execute THEN
        m.bStationAborted := FALSE;
        m.bRequestPosted := FALSE;
        m.nStation := 0;
        m.eState := E_MoverState.Ready;
    END_IF
    RETURN;
END_IF
IF NOT m.bRequestPosted THEN
    IF _bExecute AND eSt = E_State.Execute AND _Routable(s) THEN _PostRequest(i, TRUE); END_IF
    RETURN;
END_IF
IF aStation[s]^.Response.nReqId <> m.nReqId THEN RETURN; END_IF
CASE aStation[s]^.Response.eStatus OF
    E_OpStatus.Done, E_OpStatus.Failed:
        _FinishOperation(i);
    E_OpStatus.Aborted:
        IF aStation[s]^.Response.nErrorCode = 907 THEN
            // Préparation annulée par l'arrêt : rien n'a touché la pièce, le mover reprend sa Route au Start suivant
            m.bRequestPosted := FALSE;
            m.nStation := 0;
            m.eState := E_MoverState.Ready;
        ELSE
            // Station Aborted : pièces mauvaises, la position de travail reste tenue (docs/05 § 6.4)
            FOR k := 1 TO GVL_Const.MAX_SLOTS DO
                IF m.aSlot[k].bPresent THEN
                    m.aSlot[k].aData[1] := 2;
                    m.aSlot[k].bBadHandled := FALSE;
                    m.aDefectBad[k] := TRUE;
                END_IF
            END_FOR
            m.bStationAborted := TRUE;
        END_IF
        aStation[s]^.AckResponse();
END_CASE
END_METHOD

// ================================================================== fin d'une opération (docs/05 § 5.5)

METHOD PRIVATE _FinishOperation
VAR_INPUT
    i : USINT;
END_VAR
VAR
    m : REFERENCE TO ST_MoverCtx;
    g : REFERENCE TO ST_GroupCfg;
    ins : REFERENCE TO ST_Instr;
    st : POINTER TO FB_Station;
    s : USINT;
    k : USINT;
    j : USINT;
    bFailed : BOOL;
    bBad : BOOL;
    bClosed : BOOL;
    bClosedBad : BOOL;
    nFirst : USINT;
    nLast : USINT;
    nImpute : UINT;
    nOrderIdx : USINT;
    stTrace : ST_TraceRecord;
END_VAR
m REF= aMover[i];
s := m.nStation;
st := aStation[s];
g REF= _refMachine.aGroup[m.nTargetGroup];
ins REF= _refMachine.aInstr[m.nPC];
bFailed := st^.Response.eStatus = E_OpStatus.Failed;

// 0. Sorties de l'opération vers les cibles de la Route (docs/02 § 5.5)
IF NOT bFailed THEN
    FOR k := 1 TO GVL_Const.MAX_OUTPUTS DO
        IF (SHR(st^.Response.nOutMask, k - 1) AND 1) = 1 THEN _BindOutput(i, ins.aBind[k], st^.Response.aOut[k], st^.Response.stOutPos); END_IF
    END_FOR
END_IF
st^.AckResponse();

// 1. Libérer la position de travail (la file est promue par _Queued)
_refMgr.Free(m.nPos, i);
m.nPos := 0;
m.nStation := 0;
m.bRequestPosted := FALSE;

IF m.nTargetSlot = 0 THEN nFirst := 1; nLast := _refMachine.nSlots; ELSE nFirst := m.nTargetSlot; nLast := m.nTargetSlot; END_IF
FOR k := nFirst TO MIN(nLast, GVL_Const.MAX_SLOTS) DO
    // 2. Création des pièces par une opération d'entrée réussie
    IF g.eRole = E_GroupRole.Entry AND NOT bFailed AND NOT m.aSlot[k].bPresent AND nActiveOrder > 0 THEN
        m.aSlot[k].bPresent := TRUE;
        _nPartCounter := _nPartCounter + 1;
        IF LEN(m.aSlot[k].sPartId) = 0 THEN m.aSlot[k].sPartId := F_PartId(_refMachine.sMachineName, _nPartCounter); END_IF
        m.aSlot[k].eStatus := E_PartStatus.InProcess;
        m.aSlot[k].nOrder := aOrder[nActiveOrder].nOrder;
        m.aSlot[k].tCreated := F_NowUtc();
        m.aSlot[k].nOps := 0;
        m.aSlot[k].bBadHandled := FALSE;
        aOrder[nActiveOrder].nInProcess := aOrder[nActiveOrder].nInProcess + 1;
        m.aBadAtArrival[k] := FALSE;
        m.aDefectBad[k] := FALSE;
    END_IF
    IF m.aSlot[k].bPresent THEN
        IF bFailed THEN m.aSlot[k].aData[1] := 2; END_IF
        bBad := m.aSlot[k].aData[1] = 2;
        IF m.aSlot[k].nOps < GVL_Const.MAX_PART_OPS THEN
            m.aSlot[k].nOps := m.aSlot[k].nOps + 1;
            m.aSlot[k].aOps[m.aSlot[k].nOps].nStation := s;
            m.aSlot[k].aOps[m.aSlot[k].nOps].nGroup := m.nTargetGroup;
            m.aSlot[k].aOps[m.aSlot[k].nOps].eVerdictAfter := F_Verdict(m.aSlot[k].aData[1]);
        END_IF
        // 6. Surveillance des rebuts : un verdict déjà mauvais à l'arrivée ne compte pas pour cette station
        IF NOT m.aBadAtArrival[k] AND NOT m.aDefectBad[k] THEN
            _refQuality.Record(E_QualityScope.Station, s, bBad);
            _refQuality.Record(E_QualityScope.Group, m.nTargetGroup, bBad);
            nImpute := _refMachine.stQuality.aGroupImpute[m.nTargetGroup];
            IF nImpute <> 0 THEN
                FOR j := 1 TO m.aSlot[k].nOps - 1 DO
                    IF (SHR(nImpute, m.aSlot[k].aOps[j].nGroup - 1) AND 1) = 1 THEN
                        _refQuality.Record(E_QualityScope.StationImputed, m.aSlot[k].aOps[j].nStation, bBad);
                    END_IF
                END_FOR
            END_IF
        END_IF
        // 3 et 4. Clôture : pièce retirée par l'opération, sortie, rebut
        bClosed := FALSE;
        bClosedBad := FALSE;
        IF m.aSlot[k].eStatus = E_PartStatus.Removed THEN
            bClosed := TRUE;
            bClosedBad := TRUE;
        ELSIF g.eRole = E_GroupRole.Exit AND NOT bFailed THEN
            bClosed := TRUE;
            m.aSlot[k].eStatus := E_PartStatus.Exited;
        ELSIF g.eRole = E_GroupRole.Reject THEN
            bClosed := TRUE;
            bClosedBad := TRUE;
            m.aSlot[k].eStatus := E_PartStatus.Rejected;
        END_IF
        IF bClosed AND NOT m.aDefectBad[k] THEN
            _refQuality.Record(E_QualityScope.Mover, i, bClosedBad OR bBad);
            _refQuality.Record(E_QualityScope.Machine, 0, bClosedBad OR bBad);
        END_IF
        // 5. Trace
        MEMSET(ADR(stTrace), 0, SIZEOF(stTrace));
        stTrace.sPartId := m.aSlot[k].sPartId;
        stTrace.nOrder := m.aSlot[k].nOrder;
        stTrace.nMover := i;
        stTrace.nSlot := k;
        stTrace.nStation := s;
        stTrace.nGroup := m.nTargetGroup;
        stTrace.nOperation := m.nTargetOp;
        stTrace.tStart := m.tOpStart;
        stTrace.tEnd := F_NowUtc();
        stTrace.eVerdict := F_Verdict(m.aSlot[k].aData[1]);
        stTrace.nErrorCode := st^.Response.nErrorCode;
        stTrace.aData := m.aSlot[k].aData;
        _refTraces.Add(stTrace);
        IF bClosed THEN
            nOrderIdx := _OrderIndex(m.aSlot[k].nOrder);
            IF nOrderIdx > 0 THEN _CloseOrderPart(nOrderIdx, bClosedBad OR bBad); END_IF
            MEMSET(ADR(m.aSlot[k]), 0, SIZEOF(m.aSlot[k]));
            m.aDefectBad[k] := FALSE;
            m.aBadAtArrival[k] := FALSE;
        END_IF
    END_IF
END_FOR
IF _Empty(i) THEN _ResetOnEmpty(i); END_IF

// 7. Avancer le PC ; le parcage d'un mover verrouillé et vide est traité au cycle suivant
m.nPC := m.nPC + 1;
m.eState := E_MoverState.Ready;
m.nTargetGroup := 0;
END_METHOD

METHOD PRIVATE _BindOutput
VAR_INPUT
    i : USINT;
    stBind : ST_Bind;
    fValue : LREAL;
    stPos : ST_Position;
END_VAR
VAR
    n : INT;
    k : USINT;
END_VAR
n := stBind.nIndex;
CASE stBind.eTarget OF
    E_BindTarget.PartData:
        FOR k := 1 TO GVL_Const.MAX_SLOTS DO
            IF aMover[i].aSlot[k].bPresent AND n >= 1 AND n <= GVL_Const.MAX_PART_DATA AND (aMover[i].nTargetSlot = 0 OR aMover[i].nTargetSlot = k) THEN
                aMover[i].aSlot[k].aData[n] := fValue;
            END_IF
        END_FOR
    E_BindTarget.Verdict:
        FOR k := 1 TO GVL_Const.MAX_SLOTS DO
            IF aMover[i].aSlot[k].bPresent AND (aMover[i].nTargetSlot = 0 OR aMover[i].nTargetSlot = k) THEN aMover[i].aSlot[k].aData[1] := fValue; END_IF
        END_FOR
    E_BindTarget.MoverData:
        IF n >= 1 AND n <= _refMachine.nMoverVar THEN
            IF _refMachine.aMoverVar[n].eType = E_VarType.Position THEN aMover[i].aVarPos[n] := stPos; ELSE aMover[i].aVar[n] := fValue; END_IF
        END_IF
END_CASE
END_METHOD

// Variables de mover à règle « quand le mover est vide » (docs/05 § 5.8)
METHOD PRIVATE _ResetOnEmpty
VAR_INPUT
    i : USINT;
END_VAR
VAR
    v : USINT;
END_VAR
FOR v := 1 TO _refMachine.nMoverVar DO
    IF _refMachine.aMoverVar[v].eReset = E_MoverVarReset.OnEmpty THEN
        aMover[i].aVar[v] := _refMachine.aMoverVar[v].fInit;
        aMover[i].aVarPos[v] := _refMachine.aMoverVar[v].stInitPos;
    END_IF
END_FOR
END_METHOD

METHOD PRIVATE _ResetVars
VAR_INPUT
    i : USINT;
    nRank : INT;
END_VAR
VAR
    v : INT;
END_VAR
FOR v := 1 TO _refMachine.nMoverVar DO
    IF (nRank = 0 AND _refMachine.aMoverVar[v].eReset <> E_MoverVarReset.Never) OR nRank = v THEN
        aMover[i].aVar[v] := _refMachine.aMoverVar[v].fInit;
        aMover[i].aVarPos[v] := _refMachine.aMoverVar[v].stInitPos;
    END_IF
END_FOR
END_METHOD

METHOD PRIVATE _Restart
VAR_INPUT
    i : USINT;
END_VAR
aMover[i].nTable := 1;
aMover[i].nDepth := 0;
aMover[i].nPC := _refMachine.aTable[1].nStart;
aMover[i].nTargetGroup := 0;
aMover[i].eState := E_MoverState.Ready;
aMover[i].eWait := E_WaitReason.None;
aMover[i].bToEmptyPark := FALSE;
END_METHOD

// ================================================================== valeurs et conditions du process général

METHOD PRIVATE _FirstPart : POINTER TO ST_Part
VAR_INPUT
    i : USINT;
END_VAR
VAR
    k : USINT;
END_VAR
_FirstPart := ADR(aMover[i].aSlot[1]);
FOR k := 1 TO GVL_Const.MAX_SLOTS DO
    IF aMover[i].aSlot[k].bPresent THEN _FirstPart := ADR(aMover[i].aSlot[k]); RETURN; END_IF
END_FOR
END_METHOD

METHOD PRIVATE _Resolve : LREAL
VAR_INPUT
    i : USINT;
    stVal : ST_Value;
END_VAR
VAR
    pPart : POINTER TO ST_Part;
    n : INT;
    nIdx : USINT;
END_VAR
n := stVal.nIndex;
CASE stVal.eSource OF
    E_ValueSource.Const:
        _Resolve := stVal.fValue;
    E_ValueSource.PartData:
        pPart := _FirstPart(i);
        IF n >= 1 AND n <= GVL_Const.MAX_PART_DATA THEN _Resolve := pPart^.aData[n]; END_IF
    E_ValueSource.MoverData:
        IF n >= 1 AND n <= GVL_Const.MAX_MOVER_VARS THEN _Resolve := aMover[i].aVar[n]; END_IF
    E_ValueSource.OrderParam:
        // OF de la première pièce présente, sinon l'OF actif (opération d'entrée) (docs/05 § 5.6)
        pPart := _FirstPart(i);
        nIdx := 0;
        IF pPart^.bPresent THEN nIdx := _OrderIndex(pPart^.nOrder); END_IF
        IF nIdx = 0 THEN nIdx := nActiveOrder; END_IF
        IF nIdx > 0 AND n >= 1 AND n <= GVL_Const.MAX_ORDER_PARAMS THEN _Resolve := aOrder[nIdx].aParam[n]; END_IF
    E_ValueSource.System:
        CASE n OF
            1: _Resolve := 1.0;                                     // mover présent
            3:
                pPart := _FirstPart(i);
                IF pPart^.bPresent THEN _Resolve := UDINT_TO_LREAL(pPart^.nOrder); END_IF
        END_CASE
    E_ValueSource.Speed:
        _Resolve := INT_TO_LREAL(n);
END_CASE
END_METHOD

METHOD PRIVATE _EvalCond : BOOL
VAR_INPUT
    i : USINT;
    n : INT;
END_VAR
VAR
    k : USINT;
    r : BOOL;
    bAll : BOOL;
END_VAR
IF n < 1 OR n > GVL_Const.MAX_COND THEN _EvalCond := TRUE; RETURN; END_IF
IF _refMachine.aCond[n].nTerms = 0 THEN _EvalCond := TRUE; RETURN; END_IF
bAll := _refMachine.aCond[n].eCombine = E_Combine.All;
FOR k := 1 TO MIN(_refMachine.aCond[n].nTerms, GVL_Const.MAX_COND_TERMS) DO
    r := F_Compare(_Resolve(i, _refMachine.aCond[n].aTerm[k].stLeft), _refMachine.aCond[n].aTerm[k].eCmp, _Resolve(i, _refMachine.aCond[n].aTerm[k].stRight));
    IF bAll AND NOT r THEN _EvalCond := FALSE; RETURN; END_IF
    IF NOT bAll AND r THEN _EvalCond := TRUE; RETURN; END_IF
END_FOR
_EvalCond := bAll;
END_METHOD

// Un Si qui teste le verdict traite lui-même la pièce mauvaise : pas de redirection automatique (docs/02 § 6.2)
METHOD PRIVATE _MarkVerdictTested
VAR_INPUT
    i : USINT;
    n : INT;
END_VAR
VAR
    k : USINT;
    s : USINT;
    bTested : BOOL;
END_VAR
IF n < 1 OR n > GVL_Const.MAX_COND THEN RETURN; END_IF
FOR k := 1 TO MIN(_refMachine.aCond[n].nTerms, GVL_Const.MAX_COND_TERMS) DO
    bTested := bTested
        OR (_refMachine.aCond[n].aTerm[k].stLeft.eSource = E_ValueSource.PartData AND _refMachine.aCond[n].aTerm[k].stLeft.nIndex = 1)
        OR (_refMachine.aCond[n].aTerm[k].stRight.eSource = E_ValueSource.PartData AND _refMachine.aCond[n].aTerm[k].stRight.nIndex = 1);
END_FOR
// Seul un test fait pendant que la pièce est mauvaise la traite (« depuis qu'il est devenu mauvais »)
IF bTested THEN
    FOR s := 1 TO GVL_Const.MAX_SLOTS DO
        IF aMover[i].aSlot[s].bPresent AND aMover[i].aSlot[s].aData[1] = 2 THEN aMover[i].aSlot[s].bBadHandled := TRUE; END_IF
    END_FOR
END_IF
END_METHOD

METHOD PRIVATE _SetVar
VAR_INPUT
    i : USINT;
END_VAR
VAR
    ins : REFERENCE TO ST_Instr;
    pPart : POINTER TO ST_Part;
    fValue : LREAL;
    n : INT;
END_VAR
ins REF= _refMachine.aInstr[aMover[i].nPC];
fValue := _Resolve(i, ins.aParam[1]);
n := ins.aBind[1].nIndex;
CASE ins.aBind[1].eTarget OF
    E_BindTarget.PartData:
        pPart := _FirstPart(i);
        IF pPart^.bPresent AND n >= 1 AND n <= GVL_Const.MAX_PART_DATA THEN pPart^.aData[n] := F_ApplyOp(pPart^.aData[n], fValue, ins.aArg[1]); END_IF
    E_BindTarget.Verdict:
        pPart := _FirstPart(i);
        IF pPart^.bPresent THEN pPart^.aData[1] := fValue; END_IF
    E_BindTarget.MoverData:
        IF n >= 1 AND n <= GVL_Const.MAX_MOVER_VARS THEN aMover[i].aVar[n] := F_ApplyOp(aMover[i].aVar[n], fValue, ins.aArg[1]); END_IF
END_CASE
END_METHOD

METHOD PRIVATE _Empty : BOOL
VAR_INPUT
    i : USINT;
END_VAR
VAR
    k : USINT;
END_VAR
_Empty := TRUE;
FOR k := 1 TO GVL_Const.MAX_SLOTS DO
    IF aMover[i].aSlot[k].bPresent THEN _Empty := FALSE; END_IF
END_FOR
END_METHOD

// ================================================================== OF (docs/05 § 5.6)

METHOD Activate : E_RejectReason
VAR_INPUT
    stReq : ST_OrderRequest;
END_VAR
VAR
    nIdx : USINT;
    k : USINT;
    bNewReference : BOOL;
END_VAR
nIdx := _OrderIndex(stReq.nOrder);
IF nIdx = 0 THEN
    FOR k := 1 TO GVL_Const.MAX_ORDERS DO
        // Place libre, ou OF terminé ou suspendu sans pièce en cours (il quitte la table, la HMI garde ses compteurs)
        IF nIdx = 0 AND k <> nActiveOrder AND (aOrder[k].eState = E_OrderState.Free
           OR ((aOrder[k].eState = E_OrderState.Done OR aOrder[k].eState = E_OrderState.Suspended) AND aOrder[k].nInProcess = 0)) THEN
            nIdx := k;
        END_IF
    END_FOR
    IF nIdx = 0 THEN Activate := E_RejectReason.TableFull; RETURN; END_IF
    MEMSET(ADR(aOrder[nIdx]), 0, SIZEOF(aOrder[nIdx]));
    aOrder[nIdx].nOrder := stReq.nOrder;
    aOrder[nIdx].nGood := stReq.nGoodDone;
    aOrder[nIdx].nBad := stReq.nBadDone;
END_IF
bNewReference := nActiveOrder = 0 OR_ELSE aOrder[nActiveOrder].sReference <> stReq.sReference;
IF nActiveOrder > 0 AND nActiveOrder <> nIdx AND aOrder[nActiveOrder].eState = E_OrderState.Active THEN
    aOrder[nActiveOrder].eState := E_OrderState.Suspended;
END_IF
aOrder[nIdx].sNumber := stReq.sNumber;
aOrder[nIdx].sReference := stReq.sReference;
aOrder[nIdx].nQuantity := stReq.nQuantity;
aOrder[nIdx].aParam := stReq.aParam;
aOrder[nIdx].eState := E_OrderState.Active;
nActiveOrder := nIdx;
_bOrderDonePending := FALSE;
IF bNewReference THEN _refQuality.ResetAllWindows(); END_IF      // D87
FOR k := 1 TO GVL_Const.MAX_STATIONS DO
    IF aStation[k] <> 0 THEN aStation[k]^.OnOrderChange(); END_IF
END_FOR
Activate := E_RejectReason.None;
END_METHOD

METHOD Suspend : E_RejectReason
IF nActiveOrder = 0 THEN Suspend := E_RejectReason.NotConfigured; RETURN; END_IF
IF aOrder[nActiveOrder].eState = E_OrderState.Active THEN aOrder[nActiveOrder].eState := E_OrderState.Suspended; END_IF
nActiveOrder := 0;
Suspend := E_RejectReason.None;
END_METHOD

METHOD Cancel : E_RejectReason
VAR_INPUT
    nOrder : UDINT;
END_VAR
VAR
    nIdx : USINT;
END_VAR
nIdx := _OrderIndex(nOrder);
IF nIdx = 0 THEN Cancel := E_RejectReason.NotConfigured; RETURN; END_IF
IF aOrder[nIdx].nInProcess > 0 THEN Cancel := E_RejectReason.PartsInProcess; RETURN; END_IF
IF nActiveOrder = nIdx THEN nActiveOrder := 0; END_IF
MEMSET(ADR(aOrder[nIdx]), 0, SIZEOF(aOrder[nIdx]));
Cancel := E_RejectReason.None;
END_METHOD

METHOD PRIVATE _OrderIndex : USINT
VAR_INPUT
    nOrder : UDINT;
END_VAR
VAR
    k : USINT;
END_VAR
_OrderIndex := 0;
IF nOrder = 0 THEN RETURN; END_IF
FOR k := 1 TO GVL_Const.MAX_ORDERS DO
    IF aOrder[k].nOrder = nOrder AND aOrder[k].eState <> E_OrderState.Free THEN _OrderIndex := k; RETURN; END_IF
END_FOR
END_METHOD

METHOD PRIVATE _CloseOrderPart
VAR_INPUT
    nIdx : USINT;
    bBad : BOOL;
END_VAR
IF aOrder[nIdx].nInProcess > 0 THEN aOrder[nIdx].nInProcess := aOrder[nIdx].nInProcess - 1; END_IF
IF bBad THEN aOrder[nIdx].nBad := aOrder[nIdx].nBad + 1; ELSE aOrder[nIdx].nGood := aOrder[nIdx].nGood + 1; END_IF
_refQuality.RecordOrder(aOrder[nIdx], bBad);
END_METHOD

METHOD PRIVATE _CheckOrders
VAR
    k : USINT;
END_VAR
// Un OF complet dont plus aucune pièce n'est en cours passe Done, actif ou suspendu (docs/05 § 5.6)
FOR k := 1 TO GVL_Const.MAX_ORDERS DO
    IF (aOrder[k].eState = E_OrderState.Active OR aOrder[k].eState = E_OrderState.Suspended)
       AND aOrder[k].nGood + aOrder[k].nInProcess >= aOrder[k].nQuantity AND aOrder[k].nInProcess = 0 THEN
        aOrder[k].eState := E_OrderState.Done;
        IF k = nActiveOrder THEN _bOrderDonePending := TRUE; END_IF
    END_IF
END_FOR
// OF actif terminé : arrêt automatique dès qu'aucune pièce d'aucun OF n'est en cours
IF _bOrderDonePending AND _bExecute AND PartsInProcess() = 0 THEN
    _bOrderDonePending := FALSE;
    bStopRequest := TRUE;
    nStopReason := 1;                               // événement « OF terminé » (raison d'arrêt)
END_IF
END_METHOD

// ================================================================== commandes HMI

// Retrait des pièces d'un mover à vider confirmé par l'opérateur (docs/02 § 10)
METHOD ConfirmRemoved : E_RejectReason
VAR_INPUT
    nMover : USINT;
END_VAR
VAR
    k : USINT;
    nIdx : USINT;
END_VAR
IF nMover < 1 OR nMover > _refMachine.nMovers THEN ConfirmRemoved := E_RejectReason.NotConfigured; RETURN; END_IF
IF aMover[nMover].eState <> E_MoverState.ToEmpty THEN ConfirmRemoved := E_RejectReason.WrongState; RETURN; END_IF
FOR k := 1 TO GVL_Const.MAX_SLOTS DO
    IF aMover[nMover].aSlot[k].bPresent THEN
        nIdx := _OrderIndex(aMover[nMover].aSlot[k].nOrder);
        IF nIdx > 0 THEN _CloseOrderPart(nIdx, TRUE); END_IF       // retrait manuel : compte dans le budget d'OF
        MEMSET(ADR(aMover[nMover].aSlot[k]), 0, SIZEOF(aMover[nMover].aSlot[k]));
    END_IF
END_FOR
_ResetOnEmpty(nMover);
IF aMover[nMover].bOutOfService OR aMover[nMover].bQualityLock THEN
    aMover[nMover].eState := E_MoverState.OutOfService;
ELSE
    _Restart(nMover);
END_IF
// Une seule alarme 911 pour la machine : elle tombe quand plus aucun mover n'est à vider
IF NOT AnyToEmpty() AND __ISVALIDREF(_refAlarms) THEN _refAlarms.Clear(0, 911); END_IF
ConfirmRemoved := E_RejectReason.None;
END_METHOD

METHOD SetMoverVar : E_RejectReason
VAR_INPUT
    stReq : ST_MoverVarRequest;
END_VAR
IF stReq.nMover < 1 OR stReq.nMover > _refMachine.nMovers OR stReq.nVar < 1 OR stReq.nVar > _refMachine.nMoverVar THEN
    SetMoverVar := E_RejectReason.NotConfigured;
    RETURN;
END_IF
IF stReq.bReset THEN
    aMover[stReq.nMover].aVar[stReq.nVar] := _refMachine.aMoverVar[stReq.nVar].fInit;
    aMover[stReq.nMover].aVarPos[stReq.nVar] := _refMachine.aMoverVar[stReq.nVar].stInitPos;
ELSIF _refMachine.aMoverVar[stReq.nVar].eType = E_VarType.Position THEN
    aMover[stReq.nMover].aVarPos[stReq.nVar] := stReq.stPos;
ELSE
    aMover[stReq.nMover].aVar[stReq.nVar] := stReq.fValue;
END_IF
SetMoverVar := E_RejectReason.None;
END_METHOD

METHOD SetOutOfService
VAR_INPUT
    nMover : USINT;
    bOut : BOOL;
    bImmediate : BOOL;
END_VAR
IF nMover < 1 OR nMover > GVL_Const.MAX_MOVERS THEN RETURN; END_IF
aMover[nMover].bOutOfService := bOut;
aMover[nMover].bImmediate := bOut AND bImmediate;
IF NOT bOut AND aMover[nMover].eState = E_MoverState.OutOfService AND NOT aMover[nMover].bQualityLock THEN _Restart(nMover); END_IF
END_METHOD

// ================================================================== état

METHOD PartsInProcess : UINT
VAR
    i : USINT;
    k : USINT;
END_VAR
PartsInProcess := 0;
FOR i := 1 TO _refMachine.nMovers DO
    FOR k := 1 TO GVL_Const.MAX_SLOTS DO
        IF aMover[i].aSlot[k].bPresent THEN PartsInProcess := PartsInProcess + 1; END_IF
    END_FOR
END_FOR
END_METHOD

// Une opération d'entrée est-elle en cours ou engagée ? (fin des pièces : on la laisse finir, docs/05 § 5.7)
METHOD EntryInProgress : BOOL
VAR
    i : USINT;
END_VAR
EntryInProgress := FALSE;
FOR i := 1 TO _refMachine.nMovers DO
    IF aMover[i].nTargetGroup >= 1 AND aMover[i].nTargetGroup <= GVL_Const.MAX_GROUPS THEN
        IF _refMachine.aGroup[aMover[i].nTargetGroup].eRole = E_GroupRole.Entry
           AND (aMover[i].bRequestPosted OR aMover[i].eState = E_MoverState.AtStation OR (aMover[i].eState = E_MoverState.Routing AND aMover[i].nStation > 0)) THEN
            EntryInProgress := TRUE;
        END_IF
    END_IF
END_FOR
END_METHOD

METHOD AnyAtStation : BOOL
VAR
    i : USINT;
END_VAR
AnyAtStation := FALSE;
FOR i := 1 TO _refMachine.nMovers DO
    IF aMover[i].eState = E_MoverState.AtStation AND aMover[i].bRequestPosted THEN AnyAtStation := TRUE; END_IF
END_FOR
END_METHOD

METHOD AnyToEmpty : BOOL
VAR
    i : USINT;
END_VAR
AnyToEmpty := FALSE;
FOR i := 1 TO _refMachine.nMovers DO
    IF aMover[i].eState = E_MoverState.ToEmpty THEN AnyToEmpty := TRUE; END_IF
END_FOR
END_METHOD

// Fonction utilisée par le process général sans station disponible (docs/05 § 5.2) ; 0 = aucune.
// bIdle : pour Resetting → Idle, chaque fonction utilisée doit avoir au moins une station en Idle.
// Une station volontairement retirée (hors production, arrêt qualité) ne compte jamais comme un défaut.
METHOD MissingGroup : USINT
VAR_INPUT
    bIdle : BOOL;
END_VAR
VAR
    g : USINT;
    s : USINT;
    bOk : BOOL;
    bWithdrawn : BOOL;
    eSt : E_State;
END_VAR
MissingGroup := 0;
FOR g := 1 TO GVL_Const.MAX_GROUPS DO
    IF _aGroupUsed[g] THEN
        bOk := FALSE;
        bWithdrawn := FALSE;
        FOR s := 1 TO GVL_Const.MAX_STATIONS DO
            IF (SHR(_refMachine.aGroup[g].nStationMask, s - 1) AND 1) = 1 AND_THEN aStation[s] <> 0 THEN
                eSt := aStation[s]^.State;
                IF aStation[s]^.OutOfProduction OR aStation[s]^.QualityLock THEN
                    bWithdrawn := TRUE;
                ELSIF bIdle THEN
                    bOk := bOk OR eSt = E_State.Idle;
                ELSE
                    bOk := bOk OR (eSt <> E_State.Aborted AND eSt <> E_State.Stopped);
                END_IF
            END_IF
        END_FOR
        IF NOT bOk AND NOT (bWithdrawn AND NOT bIdle) THEN
            MissingGroup := g;
            RETURN;
        END_IF
    END_IF
END_FOR
END_METHOD

// Fonctions visées par une Route du process général (après chaque déploiement)
METHOD ScanGroups
VAR
    k : INT;
    g : DINT;
END_VAR
MEMSET(ADR(_aGroupUsed), 0, SIZEOF(_aGroupUsed));
FOR k := 1 TO MIN(_refMachine.nInstrCount, GVL_Const.MAX_INSTR) DO
    IF _refMachine.aInstr[k].eOp = E_Op.Route THEN
        g := _refMachine.aInstr[k].aArg[1];
        IF g >= 1 AND g <= GVL_Const.MAX_GROUPS THEN _aGroupUsed[g] := TRUE; END_IF
    END_IF
END_FOR
IF _refMachine.nRejectGroup >= 1 AND _refMachine.nRejectGroup <= GVL_Const.MAX_GROUPS THEN _aGroupUsed[_refMachine.nRejectGroup] := TRUE; END_IF
END_METHOD

METHOD MoverInError : BOOL
VAR
    i : USINT;
END_VAR
MoverInError := FALSE;
FOR i := 1 TO _refMachine.nMovers DO
    IF aMover[i].eState = E_MoverState.Error THEN MoverInError := TRUE; END_IF
END_FOR
END_METHOD

METHOD ClearFaults
VAR
    i : USINT;
END_VAR
bMachineFault := FALSE;
nFaultCode := 0;
FOR i := 1 TO GVL_Const.MAX_MOVERS DO
    IF aMover[i].eState = E_MoverState.Error THEN aMover[i].eState := E_MoverState.Ready; END_IF
END_FOR
END_METHOD

METHOD PRIVATE _Publish
VAR
    i : USINT;
    k : USINT;
    stPos : ST_Position;
    ipDrv : I_PlanarDriver;
END_VAR
ipDrv := _refMgr.Driver;
FOR i := 1 TO GVL_Const.MAX_MOVERS DO
    IF ipDrv <> 0 THEN
        stPos := ipDrv.MoverPosition(i);
        aMover[i].fX := stPos.fX;
        aMover[i].fY := stPos.fY;
        aMover[i].fC := stPos.fC;
    END_IF
    GVL_Hmi.aMoverStatus[i].eState := aMover[i].eState;
    GVL_Hmi.aMoverStatus[i].eWait := aMover[i].eWait;
    GVL_Hmi.aMoverStatus[i].fX := LREAL_TO_REAL(aMover[i].fX);
    GVL_Hmi.aMoverStatus[i].fY := LREAL_TO_REAL(aMover[i].fY);
    GVL_Hmi.aMoverStatus[i].fC := LREAL_TO_REAL(aMover[i].fC);
    GVL_Hmi.aMoverStatus[i].nStation := aMover[i].nStation;
    GVL_Hmi.aMoverStatus[i].nDest := aMover[i].nDest;
    GVL_Hmi.aMoverStatus[i].nTargetGroup := aMover[i].nTargetGroup;
    GVL_Hmi.aMoverStatus[i].nTable := aMover[i].nTable;
    GVL_Hmi.aMoverStatus[i].nPC := aMover[i].nPC;
    IF aMover[i].nPC >= 1 AND aMover[i].nPC <= _refMachine.nInstrCount THEN GVL_Hmi.aMoverStatus[i].nStepId := _refMachine.aInstr[aMover[i].nPC].nStepId; END_IF
    GVL_Hmi.aMoverStatus[i].bOutOfService := aMover[i].bOutOfService;
    GVL_Hmi.aMoverStatus[i].bQualityLock := aMover[i].bQualityLock;
    GVL_Hmi.aMoverStatus[i].bAbsent := aMover[i].bAbsent;
    FOR k := 1 TO GVL_Const.MAX_SLOTS DO
        GVL_Hmi.aMoverStatus[i].aSlot[k].bPresent := aMover[i].aSlot[k].bPresent;
        GVL_Hmi.aMoverStatus[i].aSlot[k].sPartId := aMover[i].aSlot[k].sPartId;
        GVL_Hmi.aMoverStatus[i].aSlot[k].eStatus := aMover[i].aSlot[k].eStatus;
        GVL_Hmi.aMoverStatus[i].aSlot[k].eVerdict := F_Verdict(aMover[i].aSlot[k].aData[1]);
        GVL_Hmi.aMoverStatus[i].aSlot[k].nOrder := aMover[i].aSlot[k].nOrder;
    END_FOR
    GVL_Hmi.aMoverStatus[i].aVar := aMover[i].aVar;
    GVL_Hmi.aMoverStatus[i].aVarPos := aMover[i].aVarPos;
    GVL_Hmi.aMoverStatus[i].aVarId := aMover[i].aVarId;
END_FOR
END_METHOD
END_FUNCTION_BLOCK
