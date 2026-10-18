// Tout ce qui porte un override et un état : machine, station, module, objet (docs/04 § 4)
INTERFACE I_Node
PROPERTY EffectiveOverride : LREAL
GET
END_GET
END_PROPERTY
PROPERTY NodeState : E_ObjState
GET
END_GET
END_PROPERTY
END_INTERFACE
