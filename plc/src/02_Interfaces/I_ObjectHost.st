// Registre d'objets d'une station ou de la machine (docs/04 § 9), rempli par FB_ObjectFactory
INTERFACE I_ObjectHost
METHOD ClearObjects
END_METHOD
METHOD SetObject
VAR_INPUT
    nLocalId : UINT;
    ipObj : I_Object;
END_VAR
END_METHOD
END_INTERFACE
