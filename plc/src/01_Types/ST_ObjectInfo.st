// Identité d'un objet (docs/04 § 5)
{attribute 'pack_mode' := '1'}
TYPE ST_ObjectInfo :
STRUCT
    nGlobalId : UDINT;          // n° de station × 1000 + n° local (station 0 = machine)
    nStation : UINT;
    nLocalId : UINT;            // 1..64 dans la station
    nParentId : UINT;           // n° local du parent, 0 = la station
    nType : UINT;               // n° de type du catalogue
    nPoolIndex : UINT;
    nStableId : UDINT;          // identifiant stable donné par la HMI (D92)
    sName : STRING(40);
END_STRUCT
END_TYPE
