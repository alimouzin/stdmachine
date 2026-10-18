// Identité du PLC (docs/06 § 2) ; nInterfaceVersion est toujours le premier champ
{attribute 'pack_mode' := '1'}
TYPE ST_PlcIdentity :
STRUCT
    nInterfaceVersion : UINT;
    nInstrSetVersion : UINT;
    nCatalogVersion : UINT;
    sLibVersion : STRING(20);
    aPoolSize : ARRAY[1..149] OF UINT;  // par n° de type, 0 = type absent
    nRetainId : ULINT;
END_STRUCT
END_TYPE
