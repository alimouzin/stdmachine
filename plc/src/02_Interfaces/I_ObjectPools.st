// Pools d'objets (docs/04 § 9) : instances déclarées une fois pour toutes, attribuées au déploiement par
// FB_ObjectFactory. La bibliothèque fournit FB_DefaultPools ; un projet peut donner les siens.
INTERFACE I_ObjectPools
// Nombre d'éléments du pool d'un type, 0 = type absent
METHOD PoolSize : UINT
VAR_INPUT
    nType : UINT;
END_VAR
END_METHOD
// Élément nIndex (1..PoolSize) du pool d'un type
METHOD Element : I_Object
VAR_INPUT
    nType : UINT;
    nIndex : UINT;
END_VAR
END_METHOD
END_INTERFACE
