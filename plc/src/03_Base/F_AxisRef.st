// Pointeur vers l'axe NC n° nDevice du pool matériel (docs/04 § 6.2) ; 0 si absent
FUNCTION F_AxisRef : POINTER TO AXIS_REF
VAR_INPUT
    nDevice : INT;
END_VAR
IF GVL_IoRef.pAxis = 0 OR nDevice < 1 OR nDevice > GVL_IoRef.nAxes OR nDevice > GVL_Const.MAX_AXES THEN
    F_AxisRef := 0;
ELSE
    F_AxisRef := GVL_IoRef.pAxis + INT_TO_UDINT(nDevice - 1) * SIZEOF(AXIS_REF);
END_IF
