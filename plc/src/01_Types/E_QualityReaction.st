// Réaction de la surveillance des rebuts (docs/10 § 9)
{attribute 'qualified_only'}
{attribute 'strict'}
TYPE E_QualityReaction :
(
    WarnOnly := 0,
    StationHold := 1,
    MoverOut := 2,
    StopLoading := 3,
    MachineStop := 4
) USINT;
END_TYPE
