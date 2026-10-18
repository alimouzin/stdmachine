// Module libre et base des modules, type 100 (docs/03 § 9.1) : override commun et état agrégé de ses enfants
FUNCTION_BLOCK FB_Group EXTENDS FB_ObjectBase IMPLEMENTS I_Group
VAR
    _eAggregate : E_ObjState := E_ObjState.Ready;
END_VAR

METHOD SetAggregate
VAR_INPUT
    eState : E_ObjState;
END_VAR
_eAggregate := eState;
END_METHOD

PROPERTY NodeState : E_ObjState
GET
IF _eState = E_ObjState.Disabled THEN
    NodeState := E_ObjState.Disabled;
ELSE
    NodeState := _eAggregate;
END_IF
END_GET
END_PROPERTY

METHOD PROTECTED _OnStart : E_ActStatus
VAR_INPUT
    nAction : UINT;
END_VAR
_OnStart := E_ActStatus.Error;      // un module n'a pas d'action : ses fonctions sont dépliées par le compilateur
END_METHOD
END_FUNCTION_BLOCK
