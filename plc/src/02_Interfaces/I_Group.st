// Module : reçoit l'état agrégé de ses enfants, calculé par la station (docs/04 § 8)
INTERFACE I_Group
METHOD SetAggregate
VAR_INPUT
    eState : E_ObjState;
END_VAR
END_METHOD
END_INTERFACE
