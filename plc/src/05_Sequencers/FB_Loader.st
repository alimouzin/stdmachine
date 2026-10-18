// Chargeur (docs/05 § 10) : vérification de la zone d'attente (versions, sommes de contrôle, structure, pools),
// demande d'activation à FB_Machine, liste de restauration des valeurs rémanentes.
//
// Codes d'erreur de ST_DeployError.nCode (D94) :
//   1 jeu d'instructions demandé trop récent (nDetail = version demandée)   2 catalogue demandé trop récent
//   3 somme de contrôle fausse (nStation = bloc, 17 = recettes ; nDetail = CRC calculé)
//   4 type inconnu du catalogue du PLC (nDetail = type)   5 n° local en double, hors limites ou hors rang
//   6 parent invalide (absent ou rangé après l'enfant)   7 pool insuffisant ou type absent de la bibliothèque
//   8 signal ou élément matériel invalide (nDetail = n° de signal, 100 = élément matériel)
//   9 sortie écrite par deux objets (nDetail = index DO, 10000 + index AO)   10 instruction inconnue (nDetail = n° d'opération)
//   11 saut hors de la table   12 objet invalide   13 action invalide pour le type (nDetail = action)
//   14 condition absente   15 texte absent   16 position nommée absente   17 Fork/Join non appariés
//   18 profondeur d'appel supérieure à 4   19 table invalide (début, longueur, nature ; nDetail = table)
//   20 configuration refusée par l'objet   21 machine pas Stopped
//   22 configuration machine invalide (nDetail : 1 movers, 2 emplacements, 3 positions planaires, 4 parcage,
//      5 position d'une station, 6 fonction de rebut, 7 fonction d'une Route, 8 station d'une calibration)
FUNCTION_BLOCK FB_Loader
VAR_INPUT
    bMachineStopped : BOOL;
END_VAR
VAR_OUTPUT
    bActivate : BOOL;               // impulsion : zone d'attente vérifiée, FB_Machine l'active ce cycle puis appelle Finish
    bRestore : BOOL;                // impulsion : liste de restauration complète, FB_Machine l'applique puis appelle RestoreDone
    bBusy : BOOL;
    nRestoreCount : UINT;           // liste de restauration reçue (docs/05 § 10, étape 6)
    aRestore : ARRAY[1..2048] OF ST_RetainedItem;
    sRestoreUser : STRING(24);
END_VAR
VAR
    _refFactory : REFERENCE TO FB_ObjectFactory;
    fbCrc : FB_Crc32;
    _nLastSeq : UDINT;
    _stReq : ST_DeployRequest;
    _stResp : ST_DeployResponse;
    _nStep : USINT;                 // 0 libre, 1 sommes de contrôle, 2 structure, 3 activation demandée
    _nBlock : USINT;
    _bVerified : BOOL;
    _aDoUsed : ARRAY[0..(GVL_Const.MAX_DO / 32) - 1] OF DWORD;
    _aAoUsed : ARRAY[0..(GVL_Const.MAX_AO / 32) - 1] OF DWORD;
    // Restauration (docs/05 § 10, étape 6)
    _nLastRestoreSeq : UDINT;
    _nRestorePage : UINT;
END_VAR
VAR CONSTANT
    CRC_BUDGET : UDINT := 65536;    // octets par cycle
END_VAR

METHOD Init
VAR_INPUT
    refFactory : REFERENCE TO FB_ObjectFactory;
END_VAR
_refFactory REF= refFactory;
_nLastSeq := GVL_Hmi.stDeploy.nSeq;
_nLastRestoreSeq := GVL_Hmi.stRestore.nSeq;
END_METHOD

METHOD Cycle
VAR
    bDone : BOOL;
END_VAR
bActivate := FALSE;
bRestore := FALSE;
_Restore();

CASE _nStep OF
    0:
        IF GVL_Hmi.stDeploy.nSeq = _nLastSeq THEN RETURN; END_IF
        _nLastSeq := GVL_Hmi.stDeploy.nSeq;
        _stReq := GVL_Hmi.stDeploy;
        MEMSET(ADR(_stResp), 0, SIZEOF(_stResp));
        _stResp.nSeq := _stReq.nSeq;
        CASE _stReq.eCmd OF
            E_DeployCmd.Cancel:
                _bVerified := FALSE;
                _Respond(TRUE);
            E_DeployCmd.Verify, E_DeployCmd.Activate:
                _bVerified := FALSE;
                IF NOT bMachineStopped THEN
                    F_AddDeployError(_stResp, 21, 0, 0, 0, 0, 0);
                    _Respond(FALSE);
                    RETURN;
                END_IF
                IF _stReq.nInstrSetRequired > GVL_Const.INSTR_SET_VERSION THEN
                    F_AddDeployError(_stResp, 1, 0, 0, 0, GVL_Const.INSTR_SET_VERSION, _stReq.nInstrSetRequired);
                END_IF
                IF _stReq.nCatalogRequired > GVL_Catalog.CatalogVersion THEN
                    F_AddDeployError(_stResp, 2, 0, 0, 0, GVL_Catalog.CatalogVersion, _stReq.nCatalogRequired);
                END_IF
                IF _stResp.nErrors > 0 THEN _Respond(FALSE); RETURN; END_IF
                _nBlock := 0;
                _BeginCrc(0);
                _nStep := 1;
        ELSE
            _Respond(FALSE);
        END_CASE

    1:  // Sommes de contrôle : un bloc après l'autre, CRC_BUDGET octets par cycle
        bDone := fbCrc.Step(CRC_BUDGET);
        IF bDone THEN
            IF fbCrc.Value <> _ExpectedCrc(_nBlock) THEN F_AddDeployError(_stResp, 3, _nBlock, 0, 0, 0, fbCrc.Value); END_IF
            IF _nBlock >= GVL_Const.MAX_STATIONS + 1 THEN
                IF _stResp.nErrors > 0 THEN _Respond(FALSE); RETURN; END_IF
                MEMSET(ADR(_aDoUsed), 0, SIZEOF(_aDoUsed));
                MEMSET(ADR(_aAoUsed), 0, SIZEOF(_aAoUsed));
                _nBlock := 0;
                _nStep := 2;
            ELSE
                _nBlock := _nBlock + 1;
                _BeginCrc(_nBlock);
            END_IF
        END_IF

    2:  // Structure : un bloc par cycle, puis les pools
        IF _nBlock = 0 THEN
            _CheckMachine();
        ELSE
            _CheckStation(_nBlock);
        END_IF
        IF _nBlock < GVL_Const.MAX_STATIONS THEN
            _nBlock := _nBlock + 1;
        ELSE
            _refFactory.Check(GVL_Hmi.stStaging, _stResp);
            IF _stResp.nErrors > 0 THEN
                _Respond(FALSE);
            ELSIF _stReq.eCmd = E_DeployCmd.Verify THEN
                _bVerified := TRUE;
                _Respond(TRUE);
            ELSIF NOT bMachineStopped THEN
                F_AddDeployError(_stResp, 21, 0, 0, 0, 0, 0);
                _Respond(FALSE);
            ELSE
                bActivate := TRUE;
                _nStep := 3;
            END_IF
        END_IF

    3:  // FB_Machine active et appelle Finish
        ;
END_CASE
bBusy := _nStep <> 0;
END_METHOD

// Fin de l'activation par FB_Machine (docs/05 § 10, étapes 4 et 5)
METHOD Finish
VAR_IN_OUT
    stFactoryResp : ST_DeployResponse;
END_VAR
VAR
    k : USINT;
END_VAR
FOR k := 1 TO stFactoryResp.nErrors DO
    F_AddDeployError(_stResp, stFactoryResp.aError[k].nCode, stFactoryResp.aError[k].nStation, stFactoryResp.aError[k].nTable,
                     stFactoryResp.aError[k].nInstr, stFactoryResp.aError[k].nObject, stFactoryResp.aError[k].nDetail);
END_FOR
_Respond(_stResp.nErrors = 0);
END_METHOD

PROPERTY Request : REFERENCE TO ST_DeployRequest
GET
Request REF= _stReq;
END_GET
END_PROPERTY

METHOD PRIVATE _Respond
VAR_INPUT
    bOk : BOOL;
END_VAR
_stResp.eResult := SEL(bOk, E_CmdResult.Rejected, E_CmdResult.Accepted);
GVL_Hmi.stDeployResp := _stResp;
_nStep := 0;
bBusy := FALSE;
END_METHOD

METHOD PRIVATE _BeginCrc
VAR_INPUT
    nBlock : USINT;
END_VAR
IF nBlock = 0 THEN
    fbCrc.Begin(ADR(GVL_Hmi.stStaging.stMachine), SIZEOF(GVL_Hmi.stStaging.stMachine));
ELSIF nBlock <= GVL_Const.MAX_STATIONS THEN
    fbCrc.Begin(ADR(GVL_Hmi.stStaging.aStation[nBlock]), SIZEOF(GVL_Hmi.stStaging.aStation[nBlock]));
ELSE
    fbCrc.Begin(ADR(GVL_Hmi.stStaging.aPressRecipe), SIZEOF(GVL_Hmi.stStaging.aPressRecipe));
END_IF
END_METHOD

METHOD PRIVATE _ExpectedCrc : UDINT
VAR_INPUT
    nBlock : USINT;
END_VAR
IF nBlock = 0 THEN
    _ExpectedCrc := _stReq.nCrcMachine;
ELSIF nBlock <= GVL_Const.MAX_STATIONS THEN
    _ExpectedCrc := _stReq.aCrcStation[nBlock];
ELSE
    _ExpectedCrc := _stReq.nCrcRecipes;
END_IF
END_METHOD

// ------------------------------------------------------------------ structure

METHOD PRIVATE _CheckMachine
VAR
    m : REFERENCE TO ST_MachineData;
    s : USINT;
    q : USINT;
    k : INT;
    g : DINT;
END_VAR
m REF= GVL_Hmi.stStaging.stMachine;
IF m.nMovers > GVL_Const.MAX_MOVERS THEN F_AddDeployError(_stResp, 22, 0, 0, 0, 0, 1); END_IF
IF m.nSlots < 1 OR m.nSlots > GVL_Const.MAX_SLOTS THEN F_AddDeployError(_stResp, 22, 0, 0, 0, 0, 2); END_IF
IF m.nPlanarPos < 0 OR m.nPlanarPos > GVL_Const.MAX_PLANAR_POS THEN F_AddDeployError(_stResp, 22, 0, 0, 0, 0, 3); RETURN; END_IF
IF m.nParkPos < 0 OR m.nParkPos > m.nPlanarPos OR_ELSE (m.nParkPos > 0 AND_THEN m.aPlanarPos[m.nParkPos].eKind <> E_PlanarPosKind.Park) THEN
    F_AddDeployError(_stResp, 22, 0, 0, 0, 0, 4);
END_IF
FOR s := 1 TO GVL_Const.MAX_STATIONS DO
    IF m.aStation[s].bUsed THEN
        IF m.aStation[s].nWorkPos < 1 OR m.aStation[s].nWorkPos > m.nPlanarPos THEN F_AddDeployError(_stResp, 22, s, 0, 0, 0, 5); END_IF
        FOR q := 1 TO GVL_Const.MAX_QUEUE DO
            IF m.aStation[s].aQueuePos[q] < 0 OR m.aStation[s].aQueuePos[q] > m.nPlanarPos THEN F_AddDeployError(_stResp, 22, s, 0, 0, q, 5); END_IF
        END_FOR
    END_IF
END_FOR
IF m.nRejectGroup > GVL_Const.MAX_GROUPS OR_ELSE (m.nRejectGroup > 0 AND_THEN m.aGroup[m.nRejectGroup].eRole <> E_GroupRole.Reject) THEN
    F_AddDeployError(_stResp, 22, 0, 0, 0, 0, 6);
END_IF
FOR k := 1 TO MIN(m.nInstrCount, GVL_Const.MAX_INSTR) DO
    IF m.aInstr[k].eOp = E_Op.Route THEN
        g := m.aInstr[k].aArg[1];
        IF g < 1 OR g > GVL_Const.MAX_GROUPS OR_ELSE NOT m.aGroup[g].bUsed THEN F_AddDeployError(_stResp, 22, 0, 0, k, 0, 7); END_IF
    ELSIF m.aInstr[k].eOp = E_Op.CalibStation THEN
        g := m.aInstr[k].aArg[1];
        IF g < 1 OR g > GVL_Const.MAX_STATIONS OR_ELSE NOT m.aStation[g].bUsed THEN F_AddDeployError(_stResp, 22, 0, 0, k, 0, 8); END_IF
    END_IF
END_FOR
_CheckObjects(0, ADR(m.aMachineObj), m.nMachineObj, GVL_Const.MAX_MACHINE_OBJ);
_CheckTables(0, ADR(m.aTable), ADR(m.aInstr), m.nInstrCount, ADR(m.aCond), ADR(m.aText), ADR(m.aMachineObj), m.nMachineObj);
END_METHOD

METHOD PRIVATE _CheckStation
VAR_INPUT
    n : USINT;
END_VAR
VAR
    d : REFERENCE TO ST_StationData;
    k : USINT;
    t : USINT;
END_VAR
d REF= GVL_Hmi.stStaging.aStation[n];
IF NOT GVL_Hmi.stStaging.stMachine.aStation[n].bUsed AND d.nObjCount = 0 THEN RETURN; END_IF
_CheckObjects(n, ADR(d.aObj), d.nObjCount, GVL_Const.MAX_OBJ_PER_STATION);
_CheckTables(n, ADR(d.aTable), ADR(d.aInstr), d.nInstrCount, ADR(d.aCond), ADR(d.aText), ADR(d.aObj), d.nObjCount);
FOR k := 1 TO MIN(d.nOpMapCount, GVL_Const.MAX_OPMAP) DO
    t := d.aOpMap[k].nTable;
    IF d.aOpMap[k].nGroup < 1 OR d.aOpMap[k].nGroup > GVL_Const.MAX_GROUPS OR d.aOpMap[k].nOperationNo < 1 OR d.aOpMap[k].nOperationNo > 8
       OR t < 1 OR t > GVL_Const.MAX_TABLES OR_ELSE NOT d.aTable[t].bUsed OR_ELSE d.aTable[t].eKind <> E_TableKind.Operation THEN
        F_AddDeployError(_stResp, 19, n, t, 0, 0, k);
    END_IF
END_FOR
FOR k := 1 TO MIN(d.nCalibCount, GVL_Const.CALIB_PER_STATION) DO
    t := d.aCalib[k].nTable;
    IF t < 1 OR t > GVL_Const.MAX_TABLES OR_ELSE NOT d.aTable[t].bUsed OR_ELSE d.aTable[t].eKind <> E_TableKind.Calibration THEN
        F_AddDeployError(_stResp, 19, n, t, 0, 0, 100 + k);
    END_IF
END_FOR
END_METHOD

METHOD PRIVATE _CheckObjects
VAR_INPUT
    n : USINT;
    pObj : POINTER TO ARRAY[1..GVL_Const.MAX_OBJ_PER_STATION] OF ST_ObjConfig;
    nCount : UINT;
    nMax : UINT;
END_VAR
VAR
    k : UINT;
    s : USINT;
    o : REFERENCE TO ST_ObjConfig;
    idx : INT;
    bReal : BOOL;
END_VAR
IF nCount > nMax THEN
    F_AddDeployError(_stResp, 5, n, 0, 0, nCount, nMax);
    RETURN;
END_IF
FOR k := 1 TO nCount DO
    o REF= pObj^[k];
    // Le compilateur range les objets par n° local, parents avant enfants : rang = n° local
    IF o.nLocalId <> k THEN F_AddDeployError(_stResp, 5, n, 0, 0, k, o.nLocalId); END_IF
    IF o.nParentId >= k THEN F_AddDeployError(_stResp, 6, n, 0, 0, k, o.nParentId); END_IF
    bReal := NOT o.bSimulated AND NOT o.bDisabled;
    FOR s := 1 TO GVL_Const.MAX_SIGNALS DO
        idx := o.aSignal[s].nIndex;
        CASE o.aSignal[s].eKind OF
            E_SignalKind.DI:
                IF idx < 0 OR idx >= GVL_Const.MAX_DI THEN F_AddDeployError(_stResp, 8, n, 0, 0, k, s); END_IF
            E_SignalKind.AI:
                IF idx < 0 OR idx >= GVL_Const.MAX_AI THEN F_AddDeployError(_stResp, 8, n, 0, 0, k, s); END_IF
            E_SignalKind.DO:
                IF idx < 0 OR idx >= GVL_Const.MAX_DO THEN
                    F_AddDeployError(_stResp, 8, n, 0, 0, k, s);
                ELSIF bReal THEN
                    IF (SHR(_aDoUsed[idx / 32], idx MOD 32) AND 1) = 1 THEN F_AddDeployError(_stResp, 9, n, 0, 0, k, INT_TO_UDINT(idx)); END_IF
                    _aDoUsed[idx / 32] := _aDoUsed[idx / 32] OR SHL(DWORD#1, idx MOD 32);
                END_IF
            E_SignalKind.AO:
                IF idx < 0 OR idx >= GVL_Const.MAX_AO THEN
                    F_AddDeployError(_stResp, 8, n, 0, 0, k, s);
                ELSIF bReal THEN
                    IF (SHR(_aAoUsed[idx / 32], idx MOD 32) AND 1) = 1 THEN F_AddDeployError(_stResp, 9, n, 0, 0, k, 10000 + INT_TO_UDINT(idx)); END_IF
                    _aAoUsed[idx / 32] := _aAoUsed[idx / 32] OR SHL(DWORD#1, idx MOD 32);
                END_IF
        END_CASE
    END_FOR
    // Élément matériel : un axe réel doit désigner un axe relié dans GVL_Hw
    IF bReal AND (o.nType = GVL_Catalog.Type_LinearAxis OR o.nType = GVL_Catalog.Type_RotaryAxis)
       AND (o.nDevice < 1 OR o.nDevice > GVL_IoRef.nAxes) THEN
        F_AddDeployError(_stResp, 8, n, 0, 0, k, 100);
    END_IF
END_FOR
END_METHOD

METHOD PRIVATE _CheckTables
VAR_INPUT
    n : USINT;
    pTable : POINTER TO ARRAY[1..GVL_Const.MAX_TABLES] OF ST_TableDir;
    pInstr : POINTER TO ARRAY[1..GVL_Const.MAX_INSTR] OF ST_Instr;
    nInstrCount : INT;
    pCond : POINTER TO ARRAY[1..GVL_Const.MAX_COND] OF ST_Condition;
    pText : POINTER TO ARRAY[1..GVL_Const.MAX_TEXTS] OF STRING(80);
    pObj : POINTER TO ARRAY[1..GVL_Const.MAX_OBJ_PER_STATION] OF ST_ObjConfig;
    nObjCount : UINT;
END_VAR
VAR
    t : USINT;
    i : INT;
    nS : INT;
    nE : INT;
    p : USINT;
    b : USINT;
    ins : REFERENCE TO ST_Instr;
    nForks : INT;
    nJoins : INT;
    aDepth : ARRAY[1..GVL_Const.MAX_TABLES] OF INT;
    nPass : INT;
    nCallee : DINT;
END_VAR
IF nInstrCount < 0 OR nInstrCount > GVL_Const.MAX_INSTR THEN
    F_AddDeployError(_stResp, 19, n, 0, 0, 0, 0);
    RETURN;
END_IF
FOR t := 1 TO GVL_Const.MAX_TABLES DO
    IF pTable^[t].bUsed THEN
        nS := pTable^[t].nStart;
        nE := pTable^[t].nStart + pTable^[t].nLength - 1;
        IF nS < 1 OR pTable^[t].nLength < 1 OR nE > nInstrCount THEN
            F_AddDeployError(_stResp, 19, n, t, 0, 0, t);
        ELSE
            nForks := 0;
            nJoins := 0;
            FOR i := nS TO nE DO
                ins REF= pInstr^[i];
                IF ins.eOp > E_Op.SetOut THEN F_AddDeployError(_stResp, 10, n, t, i, 0, 0); END_IF
                CASE ins.eOp OF
                    E_Op.Jump:
                        IF NOT _In(ins.aArg[1], nS, nE) THEN F_AddDeployError(_stResp, 11, n, t, i, 0, DINT_TO_UDINT(ins.aArg[1])); END_IF
                    E_Op.JumpIfFalse:
                        IF NOT _In(ins.aArg[2], nS, nE) THEN F_AddDeployError(_stResp, 11, n, t, i, 0, DINT_TO_UDINT(ins.aArg[2])); END_IF
                        IF NOT _CondOk(pCond, ins.aArg[1]) THEN F_AddDeployError(_stResp, 14, n, t, i, 0, DINT_TO_UDINT(ins.aArg[1])); END_IF
                    E_Op.WaitCond:
                        IF NOT _CondOk(pCond, ins.aArg[1]) THEN F_AddDeployError(_stResp, 14, n, t, i, 0, DINT_TO_UDINT(ins.aArg[1])); END_IF
                    E_Op.LoopNext:
                        IF NOT _In(ins.aArg[2], nS, nE) THEN F_AddDeployError(_stResp, 11, n, t, i, 0, DINT_TO_UDINT(ins.aArg[2])); END_IF
                    E_Op.Fork:
                        nForks := nForks + 1;
                        IF NOT _In(ins.aArg[1], nS, nE) THEN
                            F_AddDeployError(_stResp, 17, n, t, i, 0, DINT_TO_UDINT(ins.aArg[1]));
                        ELSIF pInstr^[ins.aArg[1]].eOp <> E_Op.Join OR pInstr^[ins.aArg[1]].aArg[1] <> i THEN
                            F_AddDeployError(_stResp, 17, n, t, i, 0, DINT_TO_UDINT(ins.aArg[1]));
                        END_IF
                        FOR b := 2 TO 5 DO
                            IF ins.aArg[b] <> 0 AND NOT _In(ins.aArg[b], nS, nE) THEN F_AddDeployError(_stResp, 11, n, t, i, 0, DINT_TO_UDINT(ins.aArg[b])); END_IF
                        END_FOR
                    E_Op.Join:
                        nJoins := nJoins + 1;
                    E_Op.Call:
                        IF ins.aArg[1] < 1 OR ins.aArg[1] > GVL_Const.MAX_TABLES OR_ELSE NOT pTable^[ins.aArg[1]].bUsed THEN
                            F_AddDeployError(_stResp, 19, n, t, i, 0, DINT_TO_UDINT(ins.aArg[1]));
                        END_IF
                    E_Op.Action, E_Op.SavePos:
                        IF ins.nObject < 1 OR ins.nObject > nObjCount THEN
                            F_AddDeployError(_stResp, 12, n, t, i, ins.nObject, 0);
                        ELSIF ins.eOp = E_Op.Action AND_THEN NOT F_CatalogHasAction(pObj^[ins.nObject].nType, ins.nAction) THEN
                            F_AddDeployError(_stResp, 13, n, t, i, ins.nObject, ins.nAction);
                        END_IF
                    E_Op.Alarm, E_Op.Prompt:
                        IF ins.aArg[1] < 0 OR ins.aArg[1] > GVL_Const.MAX_TEXTS THEN F_AddDeployError(_stResp, 15, n, t, i, 0, DINT_TO_UDINT(ins.aArg[1])); END_IF
                END_CASE
                IF ins.eOnFail = E_FailPolicy.Goto AND NOT _In(ins.nFailTarget, nS, nE) THEN
                    F_AddDeployError(_stResp, 11, n, t, i, 0, INT_TO_UDINT(ins.nFailTarget));
                END_IF
                FOR p := 1 TO GVL_Const.MAX_PARAMS DO
                    CASE ins.aParam[p].eSource OF
                        E_ValueSource.NamedPos:
                            IF n = 0 OR ins.aParam[p].nIndex < 1 OR ins.aParam[p].nIndex > GVL_Const.MAX_NAMED_POS THEN
                                F_AddDeployError(_stResp, 16, n, t, i, 0, INT_TO_UDINT(ins.aParam[p].nIndex));
                            END_IF
                        E_ValueSource.Text:
                            IF ins.aParam[p].nIndex < 1 OR ins.aParam[p].nIndex > GVL_Const.MAX_TEXTS THEN
                                F_AddDeployError(_stResp, 15, n, t, i, 0, INT_TO_UDINT(ins.aParam[p].nIndex));
                            END_IF
                        E_ValueSource.ObjValue:
                            IF ins.aParam[p].nIndex < 1 OR ins.aParam[p].nIndex > UINT_TO_INT(nObjCount) THEN
                                F_AddDeployError(_stResp, 12, n, t, i, INT_TO_UINT(ins.aParam[p].nIndex), p);
                            END_IF
                    END_CASE
                END_FOR
            END_FOR
            IF nForks <> nJoins THEN F_AddDeployError(_stResp, 17, n, t, 0, 0, INT_TO_UDINT(nForks)); END_IF
        END_IF
    END_IF
END_FOR
// Profondeur d'appel : plus longue chaîne de Call depuis chaque table ; une boucle d'appels la fait croître sans fin
FOR nPass := 1 TO GVL_Const.MAX_CALL_DEPTH + 2 DO
    FOR t := 1 TO GVL_Const.MAX_TABLES DO
        IF pTable^[t].bUsed AND pTable^[t].nStart >= 1 THEN
            FOR i := pTable^[t].nStart TO MIN(pTable^[t].nStart + pTable^[t].nLength - 1, nInstrCount) DO
                IF pInstr^[i].eOp = E_Op.Call THEN
                    nCallee := pInstr^[i].aArg[1];
                    IF nCallee >= 1 AND nCallee <= GVL_Const.MAX_TABLES THEN aDepth[t] := MAX(aDepth[t], aDepth[nCallee] + 1); END_IF
                END_IF
            END_FOR
        END_IF
    END_FOR
END_FOR
FOR t := 1 TO GVL_Const.MAX_TABLES DO
    IF aDepth[t] > GVL_Const.MAX_CALL_DEPTH THEN F_AddDeployError(_stResp, 18, n, t, 0, 0, INT_TO_UDINT(aDepth[t])); END_IF
END_FOR
END_METHOD

METHOD PRIVATE _In : BOOL
VAR_INPUT
    nTarget : DINT;
    nS : INT;
    nE : INT;
END_VAR
_In := nTarget >= nS AND nTarget <= nE;
END_METHOD

METHOD PRIVATE _CondOk : BOOL
VAR_INPUT
    pCond : POINTER TO ARRAY[1..GVL_Const.MAX_COND] OF ST_Condition;
    n : DINT;
END_VAR
_CondOk := n >= 1 AND n <= GVL_Const.MAX_COND AND_THEN pCond^[n].nTerms >= 1 AND pCond^[n].nTerms <= GVL_Const.MAX_COND_TERMS;
END_METHOD

// ------------------------------------------------------------------ restauration (docs/05 § 10, étape 6)

METHOD PRIVATE _Restore
VAR
    req : REFERENCE TO ST_RestoreRequest;
    k : UINT;
END_VAR
req REF= GVL_Hmi.stRestore;
IF req.nSeq = _nLastRestoreSeq THEN RETURN; END_IF
_nLastRestoreSeq := req.nSeq;
GVL_Hmi.stRestoreResp.nSeq := req.nSeq;
GVL_Hmi.stRestoreResp.nDetail := 0;
IF NOT bMachineStopped OR _nStep <> 0 THEN
    GVL_Hmi.stRestoreResp.eResult := E_CmdResult.Rejected;
    GVL_Hmi.stRestoreResp.eReason := SEL(bMachineStopped, E_RejectReason.WrongState, E_RejectReason.Busy);
    RETURN;
END_IF
IF req.nPage = 0 THEN nRestoreCount := 0; END_IF
IF req.nPage <> 0 AND req.nPage <> _nRestorePage + 1 THEN
    GVL_Hmi.stRestoreResp.eResult := E_CmdResult.Rejected;      // page perdue : la HMI recommence à la page 0
    GVL_Hmi.stRestoreResp.eReason := E_RejectReason.NotAllowed;
    RETURN;
END_IF
_nRestorePage := req.nPage;
FOR k := 1 TO MIN(req.nCount, 256) DO
    IF nRestoreCount < 2048 THEN
        nRestoreCount := nRestoreCount + 1;
        aRestore[nRestoreCount] := req.aItem[k];
    END_IF
END_FOR
sRestoreUser := req.sUser;
GVL_Hmi.stRestoreResp.eResult := E_CmdResult.Accepted;
GVL_Hmi.stRestoreResp.eReason := E_RejectReason.None;
IF req.bLast THEN bRestore := TRUE; END_IF
END_METHOD

// FB_Machine a appliqué la liste : nUnknown éléments sans correspondant ont été ignorés
METHOD RestoreDone
VAR_INPUT
    nUnknown : UINT;
END_VAR
GVL_Hmi.stRestoreResp.nDetail := nUnknown;
nRestoreCount := 0;
_nRestorePage := 0;
END_METHOD
END_FUNCTION_BLOCK
