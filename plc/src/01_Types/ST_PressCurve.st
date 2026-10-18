// Courbe de presse (docs/05 § 9)
{attribute 'pack_mode' := '1'}
TYPE ST_PressCurve :
STRUCT
    nCurveId : UDINT;
    nPoints : UINT;
    aStroke : ARRAY[1..GVL_Const.CURVE_POINTS] OF REAL;
    aForce : ARRAY[1..GVL_Const.CURVE_POINTS] OF REAL;
END_STRUCT
END_TYPE
