// Vérifier, activer ou annuler la zone d'attente (docs/05 § 10)
{attribute 'pack_mode' := '1'}
TYPE ST_DeployRequest :
STRUCT
    nSeq : UDINT;
    eCmd : E_DeployCmd;
    nCrcMachine : UDINT;
    aCrcStation : ARRAY[1..GVL_Const.MAX_STATIONS] OF UDINT;
    nCrcRecipes : UDINT;
    nInstrSetRequired : UINT;       // docs/02 § 7.3
    nCatalogRequired : UINT;
    sUser : STRING(24);
END_STRUCT
END_TYPE
