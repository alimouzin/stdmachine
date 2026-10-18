// Configuration d'un objet, écrite par la HMI au déploiement (docs/04 § 5)
{attribute 'pack_mode' := '1'}
TYPE ST_ObjConfig :
STRUCT
    nType : UINT;
    nLocalId : UINT;
    nParentId : UINT;
    nStableId : UDINT;          // empreinte de l'identifiant de l'objet dans le projet (D92)
    sName : STRING(40);
    bSimulated : BOOL;
    bDisabled : BOOL;
    fOverride : LREAL;                                          // 0..100
    aSignal : ARRAY[1..GVL_Const.MAX_SIGNALS] OF ST_SignalRef;  // S1..S8
    nDevice : INT;                                              // D : élément de pool matériel, 0 = aucun
    aParam : ARRAY[1..GVL_Const.MAX_OBJ_PARAMS] OF LREAL;       // P1..P24
    aSpeedPct : ARRAY[1..GVL_Const.MAX_SPEEDS] OF LREAL;        // V1..V8, 0 = non défini
    aTimeoutMs : ARRAY[1..GVL_Const.MAX_ACTION_TIMEOUTS] OF UDINT; // par n° d'action, 0 = défaut du catalogue
END_STRUCT
END_TYPE
