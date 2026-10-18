// Contrat de tout objet (docs/04 § 4)
INTERFACE I_Object EXTENDS I_Node
METHOD Configure : BOOL
VAR_INPUT
    refCfg : REFERENCE TO ST_ObjConfig;
    nStation : UINT;             // 0 = machine (D92 : le n° de station n'est pas dans ST_ObjConfig)
    nPoolIndex : UINT;
    ipParent : I_Node;
    ipLocks : I_InterlockProvider;
    ipAlarms : I_AlarmSink;
END_VAR
END_METHOD
METHOD Start : E_StartResult
VAR_INPUT
    nAction : UINT;
    refParams : REFERENCE TO ST_ActParams;
    bManual : BOOL;
END_VAR
END_METHOD
METHOD Abort
END_METHOD
METHOD Ack : BOOL
END_METHOD
METHOD Cycle
END_METHOD
METHOD ForceInput
VAR_INPUT
    nSignal : USINT;             // S1..S8 ; 0 = rendre la main à la simulation
    fValue : LREAL;
END_VAR
END_METHOD
PROPERTY Info : REFERENCE TO ST_ObjectInfo
GET
END_GET
END_PROPERTY
PROPERTY ActStatus : E_ActStatus
GET
END_GET
END_PROPERTY
PROPERTY Results : REFERENCE TO ST_ActResults
GET
END_GET
END_PROPERTY
PROPERTY Status : REFERENCE TO ST_ObjStatus
GET
END_GET
END_PROPERTY
END_INTERFACE
