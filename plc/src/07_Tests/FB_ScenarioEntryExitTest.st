// Scénario de bout en bout (docs/05 § 12.1, docs/06 § 6.1) avec le système planaire simulé : une machine de deux
// stations (Chargement, rôle Entrée ; Déchargement, rôle Sortie), deux movers, un OF de 3 pièces.
// Déploiement par le handshake réel (zone d'attente, sommes de contrôle, Activate), OF, Reset, Start,
// puis arrêt automatique « OF terminé » avec 3 bonnes et aucune pièce sur les movers.
FUNCTION_BLOCK FB_ScenarioEntryExitTest EXTENDS TcUnit.FB_TestSuite
VAR
    fbMachine : FB_Machine;
    fbCrc : FB_Crc32;
    nStep : INT;
    fStepMs : LREAL;
    nSeq : UDINT;
    nOrder : UDINT;                 // n° d'OF propre à chaque exécution (les OF sont persistants)
    k : INT;
END_VAR

EntryExitThreeParts();

METHOD PRIVATE EntryExitThreeParts
VAR
    m : REFERENCE TO ST_MachineData;
    n : USINT;
END_VAR
TEST('EntryExitThreeParts');
IF nStep >= 100 THEN RETURN; END_IF
fbMachine();
fStepMs := fStepMs + F_CycleMs();
CASE nStep OF
    0:  // Zone d'attente
        MEMSET(ADR(GVL_Hmi.stStaging), 0, SIZEOF(GVL_Hmi.stStaging));
        m REF= GVL_Hmi.stStaging.stMachine;
        m.sMachineName := 'ESSAI';
        m.nMovers := 2;
        m.nSlots := 1;
        m.fOverride := 100;
        m.nNoStationMs := 60000;
        m.aGroup[1].bUsed := TRUE;
        m.aGroup[1].sName := 'Chargement';
        m.aGroup[1].eRole := E_GroupRole.Entry;
        m.aGroup[1].nStationMask := 1;
        m.aGroup[1].nOperations := 1;
        m.aGroup[1].nPriority := 5;
        m.aGroup[2].bUsed := TRUE;
        m.aGroup[2].sName := 'Dechargement';
        m.aGroup[2].eRole := E_GroupRole.Exit;
        m.aGroup[2].nStationMask := 2;
        m.aGroup[2].nOperations := 1;
        m.aGroup[2].nPriority := 5;
        FOR n := 1 TO 2 DO
            m.aStation[n].bUsed := TRUE;
            m.aStation[n].nGroupMask := SHL(UINT#1, n - 1);
            m.aStation[n].nWorkPos := n;
            m.aStation[n].fOverride := 100;
        END_FOR
        m.aStation[1].sName := 'Entree';
        m.aStation[2].sName := 'Sortie';
        m.nPlanarPos := 5;
        _PlanarPos(1, 100, 100, E_PlanarPosKind.Work, 1);
        _PlanarPos(2, 400, 100, E_PlanarPosKind.Work, 2);
        _PlanarPos(3, 100, 300, E_PlanarPosKind.Buffer, 0);
        _PlanarPos(4, 400, 300, E_PlanarPosKind.Buffer, 0);
        _PlanarPos(5, 700, 300, E_PlanarPosKind.Park, 0);
        m.nParkPos := 5;
        // Process général : Route(Chargement), Route(Déchargement), retour au début
        m.aTable[1].bUsed := TRUE;
        m.aTable[1].sName := 'Process general';
        m.aTable[1].eKind := E_TableKind.General;
        m.aTable[1].nStart := 1;
        m.aTable[1].nLength := 3;
        m.nInstrCount := 3;
        m.aInstr[1].eOp := E_Op.Route;
        m.aInstr[1].aArg[1] := 1;
        m.aInstr[1].aArg[2] := 1;
        m.aInstr[2].eOp := E_Op.Route;
        m.aInstr[2].aArg[1] := 2;
        m.aInstr[2].aArg[2] := 1;
        m.aInstr[3].eOp := E_Op.Jump;
        m.aInstr[3].aArg[1] := 1;
        // Chaque station : une opération (fonction n, opération 1) réduite à End(0)
        FOR n := 1 TO 2 DO
            GVL_Hmi.stStaging.aStation[n].aTable[1].bUsed := TRUE;
            GVL_Hmi.stStaging.aStation[n].aTable[1].sName := 'Operation';
            GVL_Hmi.stStaging.aStation[n].aTable[1].eKind := E_TableKind.Operation;
            GVL_Hmi.stStaging.aStation[n].aTable[1].nStart := 1;
            GVL_Hmi.stStaging.aStation[n].aTable[1].nLength := 1;
            GVL_Hmi.stStaging.aStation[n].nInstrCount := 1;
            GVL_Hmi.stStaging.aStation[n].aInstr[1].eOp := E_Op.End;
            GVL_Hmi.stStaging.aStation[n].nOpMapCount := 1;
            GVL_Hmi.stStaging.aStation[n].aOpMap[1].nGroup := n;
            GVL_Hmi.stStaging.aStation[n].aOpMap[1].nOperationNo := 1;
            GVL_Hmi.stStaging.aStation[n].aOpMap[1].nTable := 1;
        END_FOR
        // Sommes de contrôle, calculées comme la HMI le fera
        fbCrc.Begin(ADR(GVL_Hmi.stStaging.stMachine), SIZEOF(GVL_Hmi.stStaging.stMachine));
        fbCrc.Step(16#FFFFFFFF);
        GVL_Hmi.stDeploy.nCrcMachine := fbCrc.Value;
        FOR n := 1 TO GVL_Const.MAX_STATIONS DO
            fbCrc.Begin(ADR(GVL_Hmi.stStaging.aStation[n]), SIZEOF(GVL_Hmi.stStaging.aStation[n]));
            fbCrc.Step(16#FFFFFFFF);
            GVL_Hmi.stDeploy.aCrcStation[n] := fbCrc.Value;
        END_FOR
        fbCrc.Begin(ADR(GVL_Hmi.stStaging.aPressRecipe), SIZEOF(GVL_Hmi.stStaging.aPressRecipe));
        fbCrc.Step(16#FFFFFFFF);
        GVL_Hmi.stDeploy.nCrcRecipes := fbCrc.Value;
        GVL_Hmi.stDeploy.eCmd := E_DeployCmd.Activate;
        GVL_Hmi.stDeploy.nInstrSetRequired := 1;
        GVL_Hmi.stDeploy.nCatalogRequired := 1;
        GVL_Hmi.stDeploy.sUser := 'essai';
        nSeq := GVL_Hmi.stDeploy.nSeq + 1;
        GVL_Hmi.stDeploy.nSeq := nSeq;
        _Next(1);
    1:  // Réponse du déploiement
        IF GVL_Hmi.stDeployResp.nSeq = nSeq THEN
            AssertTrue(Condition := GVL_Hmi.stDeployResp.eResult = E_CmdResult.Accepted, Message := 'Deploiement accepte');
            AssertEquals_USINT(Expected := 0, Actual := GVL_Hmi.stDeployResp.nErrors, Message := 'Aucune erreur de deploiement');
            IF GVL_Hmi.stDeployResp.nErrors > 0 THEN
                AssertEquals_UINT(Expected := 0, Actual := GVL_Hmi.stDeployResp.aError[1].nCode, Message := 'Premiere erreur de deploiement');
            END_IF
            // OF de 3 pièces
            GVL_Hmi.stOrder.eCmd := E_OrderCmd.Activate;
            nOrder := LINT_TO_UDINT((F_NowUtc() / 10000000) MOD 1000000000) + 1;
            GVL_Hmi.stOrder.nOrder := nOrder;
            GVL_Hmi.stOrder.sNumber := 'OF-ESSAI';
            GVL_Hmi.stOrder.sReference := 'REF-1';
            GVL_Hmi.stOrder.nQuantity := 3;
            nSeq := GVL_Hmi.stOrder.nSeq + 1;
            GVL_Hmi.stOrder.nSeq := nSeq;
            _Next(2);
        ELSIF fStepMs > 5000 THEN
            _Abandon('Pas de reponse au deploiement');
        END_IF
    2:
        IF GVL_Hmi.stOrderResp.nSeq = nSeq THEN
            AssertTrue(Condition := GVL_Hmi.stOrderResp.eResult = E_CmdResult.Accepted, Message := 'OF accepte');
            _Command(E_Cmd.Reset);
            _Next(3);
        ELSIF fStepMs > 1000 THEN
            _Abandon('Pas de reponse a l OF');
        END_IF
    3:  // Reset : système planaire actif, movers détectés, stations Idle
        IF GVL_Hmi.stMachineStatus.eState = E_State.Idle THEN
            _Command(E_Cmd.Start);
            _Next(4);
        ELSIF fStepMs > 10000 THEN
            _Abandon('La machine n arrive pas en Idle');
        END_IF
    4:
        IF GVL_Hmi.stMachineStatus.eState = E_State.Execute THEN
            _Next(5);
        ELSIF fStepMs > 1000 THEN
            _Abandon('La machine ne demarre pas');
        END_IF
    5:  // Production jusqu'à l'arrêt automatique « OF terminé »
        IF GVL_Hmi.stMachineStatus.eState = E_State.Stopped THEN
            AssertTrue(Condition := GVL_Hmi.stMachineStatus.eStopReason = E_StopReason.OrderComplete, Message := 'Arret automatique OF termine');
            AssertEquals_UINT(Expected := 0, Actual := GVL_Hmi.stMachineStatus.nPartsInProcess, Message := 'Aucune piece sur les movers');
            FOR k := 1 TO GVL_Const.MAX_ORDERS DO
                IF GVL_Hmi.stMachineStatus.aOrder[k].nOrder = nOrder THEN
                    AssertEquals_UDINT(Expected := 3, Actual := GVL_Hmi.stMachineStatus.aOrder[k].nGood, Message := 'Trois bonnes');
                    AssertEquals_UDINT(Expected := 0, Actual := GVL_Hmi.stMachineStatus.aOrder[k].nBad, Message := 'Aucune mauvaise');
                    AssertTrue(Condition := GVL_Hmi.stMachineStatus.aOrder[k].eState = E_OrderState.Done, Message := 'OF termine');
                END_IF
            END_FOR
            AssertTrue(Condition := GVL_Hmi.stTraces.nWriteSeq >= 6, Message := 'Une trace par operation et par piece');
            TEST_FINISHED();
            nStep := 100;
        ELSIF GVL_Hmi.stMachineStatus.eState = E_State.Aborted THEN
            _Abandon('La machine est passee Aborted');
        ELSIF fStepMs > 60000 THEN
            _Abandon('L OF ne se termine pas en 60 s');
        END_IF
END_CASE
END_METHOD

METHOD PRIVATE _PlanarPos
VAR_INPUT
    nIndex : INT;
    fX : LREAL;
    fY : LREAL;
    eKind : E_PlanarPosKind;
    nStation : USINT;
END_VAR
GVL_Hmi.stStaging.stMachine.aPlanarPos[nIndex].stPos.fX := fX;
GVL_Hmi.stStaging.stMachine.aPlanarPos[nIndex].stPos.fY := fY;
GVL_Hmi.stStaging.stMachine.aPlanarPos[nIndex].eKind := eKind;
GVL_Hmi.stStaging.stMachine.aPlanarPos[nIndex].nStation := nStation;
END_METHOD

METHOD PRIVATE _Command
VAR_INPUT
    eCmd : E_Cmd;
END_VAR
GVL_Hmi.stCmd.nTarget := 0;
GVL_Hmi.stCmd.bSetMode := FALSE;
GVL_Hmi.stCmd.eCmd := eCmd;
GVL_Hmi.stCmd.sUser := 'essai';
GVL_Hmi.stCmd.nSeq := GVL_Hmi.stCmd.nSeq + 1;
END_METHOD

METHOD PRIVATE _Next
VAR_INPUT
    nNext : INT;
END_VAR
nStep := nNext;
fStepMs := 0;
END_METHOD

METHOD PRIVATE _Fail
VAR_INPUT
    sWhy : STRING(80);
END_VAR
AssertTrue(Condition := FALSE, Message := sWhy);
TEST_FINISHED();
nStep := 100;
END_METHOD
END_FUNCTION_BLOCK
