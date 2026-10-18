// Nature d'un signal de l'image E/S (docs/04 § 5)
{attribute 'qualified_only'}
{attribute 'strict'}
TYPE E_SignalKind :
(
    None := 0,
    DI := 1,
    DO := 2,
    AI := 3,
    AO := 4
) USINT;
END_TYPE
