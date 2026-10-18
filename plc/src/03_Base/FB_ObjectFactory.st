// Fabrique d'objets (docs/04 § 9) : attribue à chaque objet configuré un élément de son pool et le configure.
// Un objet déjà présent au déploiement précédent (même nStableId) reprend le même élément, pour garder ses
// compteurs de maintenance persistants (D92). Hôte 0 = la machine, 1..16 = les stations.
FUNCTION_BLOCK FB_ObjectFactory
VAR
    _ipPools : I_ObjectPools;
    _ipAlarms : I_AlarmSink;
    _aHost : ARRAY[0..GVL_Const.MAX_STATIONS] OF I_ObjectHost;
    _aNode : ARRAY[0..GVL_Const.MAX_STATIONS] OF I_Node;
    _aLocks : ARRAY[0..GVL_Const.MAX_STATIONS] OF I_InterlockProvider;
    _aUsed : ARRAY[0..GVL_Const.MAX_TYPE, 0..7] OF DWORD;    // bit = élément de pool pris par ce déploiement
    _aAssign : ARRAY[0..GVL_Const.MAX_STATIONS, 1..GVL_Const.MAX_OBJ_PER_STATION] OF UINT;  // élément attribué
END_VAR

METHOD SetPools
VAR_INPUT
    ipPools : I_ObjectPools;
    ipAlarms : I_AlarmSink;
END_VAR
_ipPools := ipPools;
_ipAlarms := ipAlarms;
END_METHOD

METHOD SetHost
VAR_INPUT
    nHost : USINT;
    ipHost : I_ObjectHost;
    ipNode : I_Node;
    ipLocks : I_InterlockProvider;
END_VAR
IF nHost <= GVL_Const.MAX_STATIONS THEN
    _aHost[nHost] := ipHost;
    _aNode[nHost] := ipNode;
    _aLocks[nHost] := ipLocks;
END_IF
END_METHOD

// Taille du pool d'un type (identité du PLC)
METHOD PoolSize : UINT
VAR_INPUT
    nType : UINT;
END_VAR
IF _ipPools <> 0 THEN PoolSize := _ipPools.PoolSize(nType); END_IF
END_METHOD

// Vérification sans effet (docs/05 § 10, étape 3) : types connus et pools suffisants pour la zone d'attente
METHOD Check : BOOL
VAR_IN_OUT
    stStaging : ST_Staging;
    stResp : ST_DeployResponse;
END_VAR
VAR
    aNeed : ARRAY[0..GVL_Const.MAX_TYPE] OF UINT;
    n : USINT;
    k : UINT;
    nCount : UINT;
    nType : UINT;
    nPool : UINT;
END_VAR
Check := TRUE;
IF _ipPools = 0 THEN
    F_AddDeployError(stResp, 7, 0, 0, 0, 0, 0);
    Check := FALSE;
    RETURN;
END_IF
FOR n := 0 TO GVL_Const.MAX_STATIONS DO
    IF n = 0 THEN nCount := stStaging.stMachine.nMachineObj; ELSE nCount := stStaging.aStation[n].nObjCount; END_IF
    FOR k := 1 TO MIN(nCount, GVL_Const.MAX_OBJ_PER_STATION) DO
        IF n = 0 THEN
            IF k > GVL_Const.MAX_MACHINE_OBJ THEN EXIT; END_IF
            nType := stStaging.stMachine.aMachineObj[k].nType;
        ELSE
            nType := stStaging.aStation[n].aObj[k].nType;
        END_IF
        IF nType = 0 OR nType > GVL_Const.MAX_TYPE OR_ELSE (GVL_Catalog.aActionMask[nType] = 0 AND nType < GVL_Catalog.Type_Group) THEN
            F_AddDeployError(stResp, 4, n, 0, 0, k, nType);         // type inconnu du catalogue du PLC
            Check := FALSE;
        ELSE
            nPool := _ipPools.PoolId(nType);
            IF nPool <= GVL_Const.MAX_TYPE THEN aNeed[nPool] := aNeed[nPool] + 1; END_IF
        END_IF
    END_FOR
END_FOR
FOR nPool := 0 TO GVL_Const.MAX_TYPE DO
    IF aNeed[nPool] > _ipPools.PoolSize(nPool) THEN
        F_AddDeployError(stResp, 7, 0, 0, 0, aNeed[nPool], nPool);  // pool trop petit (nObject = nombre demandé)
        Check := FALSE;
    END_IF
END_FOR
END_METHOD

// Attribution et configuration de tous les objets de la zone active (machine Stopped)
METHOD Build : BOOL
VAR_IN_OUT
    stResp : ST_DeployResponse;
END_VAR
VAR
    n : USINT;
    k : UINT;
    nCount : UINT;
    idx : UINT;
    pCfg : POINTER TO ST_ObjConfig;
    ipObj : I_Object;
    ipParent : I_Node;
    bOk : BOOL;
END_VAR
Build := TRUE;
IF _ipPools = 0 THEN RETURN; END_IF
MEMSET(ADR(_aUsed), 0, SIZEOF(_aUsed));
MEMSET(ADR(_aAssign), 0, SIZEOF(_aAssign));
// 1. Reprise des éléments par identifiant stable
FOR n := 0 TO GVL_Const.MAX_STATIONS DO
    nCount := _Count(n);
    FOR k := 1 TO nCount DO
        pCfg := _Cfg(n, k);
        IF pCfg^.nStableId <> 0 THEN _aAssign[n, k] := _FindByStableId(pCfg^.nType, pCfg^.nStableId); END_IF
    END_FOR
END_FOR
// 2. Les autres prennent un élément libre, de préférence jamais attribué
FOR n := 0 TO GVL_Const.MAX_STATIONS DO
    nCount := _Count(n);
    FOR k := 1 TO nCount DO
        IF _aAssign[n, k] = 0 THEN
            pCfg := _Cfg(n, k);
            _aAssign[n, k] := _FindFree(pCfg^.nType);
            IF _aAssign[n, k] = 0 THEN
                F_AddDeployError(stResp, 7, n, 0, 0, k, pCfg^.nType);
                Build := FALSE;
            END_IF
        END_IF
    END_FOR
END_FOR
IF NOT Build THEN RETURN; END_IF
// 3. Configuration, parents d'abord (la configuration est rangée parents avant enfants)
FOR n := 0 TO GVL_Const.MAX_STATIONS DO
    IF _aHost[n] <> 0 THEN
        _aHost[n].ClearObjects();
        nCount := _Count(n);
        FOR k := 1 TO nCount DO
            pCfg := _Cfg(n, k);
            idx := _aAssign[n, k];
            ipObj := _ipPools.Element(pCfg^.nType, idx);
            ipParent := _aNode[n];
            IF pCfg^.nParentId >= 1 AND pCfg^.nParentId < k THEN
                ipParent := _ipPools.Element(_Cfg(n, pCfg^.nParentId)^.nType, _aAssign[n, pCfg^.nParentId]);
            END_IF
            bOk := ipObj <> 0;
            IF bOk THEN
                bOk := ipObj.Configure(refCfg := pCfg^, nStation := n, nPoolIndex := idx, ipParent := ipParent,
                                       ipLocks := _aLocks[n], ipAlarms := _ipAlarms);
            END_IF
            IF bOk THEN
                _aHost[n].SetObject(pCfg^.nLocalId, ipObj);
            ELSE
                F_AddDeployError(stResp, 20, n, 0, 0, pCfg^.nLocalId, pCfg^.nType);   // configuration refusée par l'objet
                Build := FALSE;
            END_IF
        END_FOR
    END_IF
END_FOR
END_METHOD

METHOD PRIVATE _Count : UINT
VAR_INPUT
    n : USINT;
END_VAR
IF n = 0 THEN
    _Count := MIN(GVL_Active.stMachine.nMachineObj, GVL_Const.MAX_MACHINE_OBJ);
ELSE
    _Count := MIN(GVL_Active.aStation[n].nObjCount, GVL_Const.MAX_OBJ_PER_STATION);
END_IF
END_METHOD

METHOD PRIVATE _Cfg : POINTER TO ST_ObjConfig
VAR_INPUT
    n : USINT;
    k : UINT;
END_VAR
IF n = 0 THEN _Cfg := ADR(GVL_Active.stMachine.aMachineObj[k]); ELSE _Cfg := ADR(GVL_Active.aStation[n].aObj[k]); END_IF
END_METHOD

METHOD PRIVATE _IsUsed : BOOL
VAR_INPUT
    nPool : UINT;
    idx : UINT;
END_VAR
_IsUsed := (SHR(_aUsed[nPool, (idx - 1) / 32], (idx - 1) MOD 32) AND 1) = 1;
END_METHOD

METHOD PRIVATE _Take
VAR_INPUT
    nPool : UINT;
    idx : UINT;
END_VAR
_aUsed[nPool, (idx - 1) / 32] := _aUsed[nPool, (idx - 1) / 32] OR SHL(DWORD#1, (idx - 1) MOD 32);
END_METHOD

METHOD PRIVATE _FindByStableId : UINT
VAR_INPUT
    nType : UINT;
    nStableId : UDINT;
END_VAR
VAR
    nPool : UINT;
    idx : UINT;
    ipObj : I_Object;
    ipMaint : I_Maintained;
END_VAR
_FindByStableId := 0;
nPool := _ipPools.PoolId(nType);
IF nPool > GVL_Const.MAX_TYPE THEN RETURN; END_IF
FOR idx := 1 TO MIN(_ipPools.PoolSize(nType), 256) DO
    IF NOT _IsUsed(nPool, idx) THEN
        ipObj := _ipPools.Element(nType, idx);
        IF ipObj <> 0 AND_THEN __QUERYINTERFACE(ipObj, ipMaint) AND_THEN ipMaint.StableId = nStableId THEN
            _Take(nPool, idx);
            _FindByStableId := idx;
            RETURN;
        END_IF
    END_IF
END_FOR
END_METHOD

METHOD PRIVATE _FindFree : UINT
VAR_INPUT
    nType : UINT;
END_VAR
VAR
    nPool : UINT;
    idx : UINT;
    nAny : UINT;
    ipObj : I_Object;
    ipMaint : I_Maintained;
END_VAR
_FindFree := 0;
nPool := _ipPools.PoolId(nType);
IF nPool > GVL_Const.MAX_TYPE THEN RETURN; END_IF
FOR idx := 1 TO MIN(_ipPools.PoolSize(nType), 256) DO
    IF NOT _IsUsed(nPool, idx) THEN
        IF nAny = 0 THEN nAny := idx; END_IF
        ipObj := _ipPools.Element(nType, idx);
        IF ipObj <> 0 AND_THEN __QUERYINTERFACE(ipObj, ipMaint) AND_THEN ipMaint.StableId = 0 THEN
            _Take(nPool, idx);
            _FindFree := idx;
            RETURN;
        END_IF
    END_IF
END_FOR
IF nAny > 0 THEN
    _Take(nPool, nAny);
    _FindFree := nAny;
END_IF
END_METHOD

// Objet d'un hôte, pour la restauration des compteurs (docs/05 § 10, étape 6)
METHOD FindObject : I_Object
VAR_INPUT
    nStableId : UDINT;
END_VAR
VAR
    n : USINT;
    k : UINT;
    pCfg : POINTER TO ST_ObjConfig;
END_VAR
FindObject := 0;
IF _ipPools = 0 OR nStableId = 0 THEN RETURN; END_IF
FOR n := 0 TO GVL_Const.MAX_STATIONS DO
    FOR k := 1 TO _Count(n) DO
        pCfg := _Cfg(n, k);
        IF pCfg^.nStableId = nStableId AND _aAssign[n, k] > 0 THEN
            FindObject := _ipPools.Element(pCfg^.nType, _aAssign[n, k]);
            RETURN;
        END_IF
    END_FOR
END_FOR
END_METHOD

// Compteurs de maintenance de tous les objets attribués, ajoutés à la liste publiée pour la HMI
METHOD PublishRetained
VAR_IN_OUT
    stOut : ST_RetainedValues;
END_VAR
VAR
    n : USINT;
    k : UINT;
    pCfg : POINTER TO ST_ObjConfig;
    ipObj : I_Object;
    ipMaint : I_Maintained;
    stItem : ST_RetainedItem;
END_VAR
IF _ipPools = 0 THEN RETURN; END_IF
FOR n := 0 TO GVL_Const.MAX_STATIONS DO
    FOR k := 1 TO _Count(n) DO
        pCfg := _Cfg(n, k);
        IF _aAssign[n, k] > 0 AND pCfg^.nStableId <> 0 THEN
            ipObj := _ipPools.Element(pCfg^.nType, _aAssign[n, k]);
            IF ipObj <> 0 AND_THEN __QUERYINTERFACE(ipObj, ipMaint) THEN
                MEMSET(ADR(stItem), 0, SIZEOF(stItem));
                stItem.eKind := E_RetainedKind.ObjectCounters;
                stItem.nStation := n;
                stItem.nId := pCfg^.nStableId;
                stItem.aValue[1] := UDINT_TO_LREAL(ipMaint.ActionCount);
                stItem.aValue[2] := UDINT_TO_LREAL(ipMaint.ErrorCount);
                stItem.aValue[3] := ipMaint.MotionHours;
                F_AddRetained(stOut, stItem);
            END_IF
        END_IF
    END_FOR
END_FOR
END_METHOD

METHOD Restore : BOOL
VAR_IN_OUT
    stItem : ST_RetainedItem;
END_VAR
VAR
    ipObj : I_Object;
    ipMaint : I_Maintained;
END_VAR
Restore := FALSE;
IF stItem.eKind <> E_RetainedKind.ObjectCounters THEN RETURN; END_IF
ipObj := FindObject(stItem.nId);
IF ipObj <> 0 AND_THEN __QUERYINTERFACE(ipObj, ipMaint) THEN
    ipMaint.RestoreCounters(LREAL_TO_UDINT(stItem.aValue[1]), LREAL_TO_UDINT(stItem.aValue[2]), stItem.aValue[3]);
    Restore := TRUE;
END_IF
END_METHOD
END_FUNCTION_BLOCK
