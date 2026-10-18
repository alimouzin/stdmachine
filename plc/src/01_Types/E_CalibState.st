// État d'une calibration (docs/05 § 10.1)
{attribute 'qualified_only'}
{attribute 'strict'}
TYPE E_CalibState :
(
    Never := 0,
    Valid := 1,
    Expired := 2,
    Failed := 3,
    Running := 4
) USINT;
END_TYPE
