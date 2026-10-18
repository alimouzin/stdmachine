// Courbes de presse (docs/05 § 9) : tampon circulaire GVL_Hmi.aCurve[1..8], dernière courbe de chaque station
// pour la trace de l'opération (ST_TraceRecord.nCurveId).
{attribute 'qualified_only'}
VAR_GLOBAL
    nNextCurveId : UDINT := 1;
    nNextSlot : USINT := 1;
    aLastCurveId : ARRAY[0..GVL_Const.MAX_STATIONS] OF UDINT;
    aLastCurveTime : ARRAY[0..GVL_Const.MAX_STATIONS] OF LINT;      // début de l'enregistrement (UTC)
END_VAR
