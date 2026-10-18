// Nœud qui porte un override et un état agrégé : machine, station (docs/04 § 8)
FUNCTION_BLOCK FB_NodeBase IMPLEMENTS I_Node
VAR
    _ipParent : I_Node;
    _fOverride : LREAL := 100.0;    // override propre du nœud
    _fEffOverride : LREAL := 100.0;
    _eState : E_ObjState := E_ObjState.Ready;
END_VAR

METHOD SetParent
VAR_INPUT
    ipParent : I_Node;
END_VAR
_ipParent := ipParent;
END_METHOD

METHOD SetOverride
VAR_INPUT
    fPercent : LREAL;
END_VAR
_fOverride := LIMIT(0.0, fPercent, 100.0);
END_METHOD

METHOD UpdateOverride
VAR
    fParent : LREAL := 100.0;
END_VAR
IF _ipParent <> 0 THEN fParent := _ipParent.EffectiveOverride; END_IF
_fEffOverride := fParent * _fOverride / 100.0;
END_METHOD

METHOD SetState
VAR_INPUT
    eState : E_ObjState;
END_VAR
_eState := eState;
END_METHOD

PROPERTY EffectiveOverride : LREAL
GET
EffectiveOverride := _fEffOverride;
END_GET
END_PROPERTY

PROPERTY NodeState : E_ObjState
GET
NodeState := _eState;
END_GET
END_PROPERTY

PROPERTY OwnOverride : LREAL
GET
OwnOverride := _fOverride;
END_GET
END_PROPERTY
END_FUNCTION_BLOCK
