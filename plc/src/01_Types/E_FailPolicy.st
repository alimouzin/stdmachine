// Politique d'échec d'une instruction Action (docs/05 § 3.6)
{attribute 'qualified_only'}
{attribute 'strict'}
TYPE E_FailPolicy :
(
    StopStation := 0,
    Retry := 1,
    PartBad := 2,
    Goto := 3,
    Ignore := 4
) USINT;
END_TYPE
