// Statut d'un mover (docs/06 § 6.3)
{attribute 'pack_mode' := '1'}
TYPE ST_MoverStatus :
STRUCT
    eState : E_MoverState;
    eWait : E_WaitReason;
    fX : REAL;
    fY : REAL;
    fC : REAL;
    nStation : USINT;
    nDest : INT;
    nTargetGroup : USINT;
    nTable : INT;
    nPC : INT;
    nStepId : UDINT;
    bOutOfService : BOOL;
    bQualityLock : BOOL;
    bAbsent : BOOL;
    aSlot : ARRAY[1..GVL_Const.MAX_SLOTS] OF ST_SlotStatus;
    aVar : ARRAY[1..GVL_Const.MAX_MOVER_VARS] OF LREAL;
    aVarPos : ARRAY[1..GVL_Const.MAX_MOVER_VARS] OF ST_Position;
    aVarId : ARRAY[1..GVL_Const.MAX_MOVER_VARS] OF UDINT;
END_STRUCT
END_TYPE
