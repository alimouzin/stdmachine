// Vrai si le bit de l'action est posé dans un masque du catalogue (GVL_Catalog.a*Mask)
FUNCTION F_CatalogBit : BOOL
VAR_INPUT
    nMask : UDINT;
    nAction : UINT;
END_VAR
IF nAction < 1 OR nAction > GVL_Const.MAX_ACTION THEN
    F_CatalogBit := FALSE;
ELSE
    F_CatalogBit := (SHR(nMask, nAction - 1) AND 1) = 1;
END_IF
