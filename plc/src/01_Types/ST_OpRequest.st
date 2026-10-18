// Demande d'opération du séquenceur général à une station (docs/05 § 4.1)
TYPE ST_OpRequest :
STRUCT
    nReqId : UDINT;                 // change à chaque nouvelle demande
    nGroup : USINT;
    nOperation : USINT;
    nMover : USINT;
    nSlot : USINT;                  // 0 = tous
    bMoverAtStation : BOOL;         // préparation : faux tant que le mover n'est pas arrivé
    bCancel : BOOL;                 // préparation annulée (Stop)
    aParam : ARRAY[1..GVL_Const.MAX_PARAMS] OF LREAL;
END_STRUCT
END_TYPE
