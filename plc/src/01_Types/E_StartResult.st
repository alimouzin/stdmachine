// Réponse de I_Object.Start (docs/04 § 5)
{attribute 'qualified_only'}
{attribute 'strict'}
TYPE E_StartResult :
(
    Accepted := 0,
    Busy := 1,
    NotReady := 2,
    UnknownAction := 3,
    BadParam := 4,
    Interlocked := 5,
    Disabled := 6,
    ManualOnly := 7,
    NotConfigured := 8
) USINT;
END_TYPE
