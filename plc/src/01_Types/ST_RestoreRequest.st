// Restauration des valeurs rémanentes, par pages de 256 (docs/05 § 10, étape 6)
{attribute 'pack_mode' := '1'}
TYPE ST_RestoreRequest :
STRUCT
    nSeq : UDINT;
    nPage : UINT;                   // 0 = première page (efface la liste en cours)
    bLast : BOOL;                   // dernière page : appliquer
    nCount : UINT;
    aItem : ARRAY[1..256] OF ST_RetainedItem;
    sUser : STRING(24);
END_STRUCT
END_TYPE
