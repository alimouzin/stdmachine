// L'action existe-t-elle pour ce type ? (docs/04 § 7.3)
FUNCTION F_CatalogHasAction : BOOL
VAR_INPUT
    nType : UINT;
    nAction : UINT;
END_VAR
IF nType > GVL_Const.MAX_TYPE THEN
    F_CatalogHasAction := FALSE;
ELSE
    F_CatalogHasAction := F_CatalogBit(GVL_Catalog.aActionMask[nType], nAction);
END_IF
