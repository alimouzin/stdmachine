// Emplacement d'un mover, publié (docs/06 § 6.3)
{attribute 'pack_mode' := '1'}
TYPE ST_SlotStatus :
STRUCT
    bPresent : BOOL;
    sPartId : STRING(40);
    eStatus : E_PartStatus;
    eVerdict : E_Verdict;
    nOrder : UDINT;
END_STRUCT
END_TYPE
