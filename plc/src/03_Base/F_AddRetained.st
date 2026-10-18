// Ajoute une valeur à la liste publiée pour la sauvegarde par la HMI (docs/02 § 7.4)
FUNCTION F_AddRetained : BOOL
VAR_IN_OUT
    stOut : ST_RetainedValues;
    stItem : ST_RetainedItem;
END_VAR
F_AddRetained := FALSE;
IF stOut.nCount >= 2048 THEN RETURN; END_IF
stOut.nCount := stOut.nCount + 1;
stOut.aItem[stOut.nCount] := stItem;
F_AddRetained := TRUE;
END_FUNCTION
