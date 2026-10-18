// Événement d'alarme (docs/14 § 5)
{attribute 'qualified_only'}
{attribute 'strict'}
TYPE E_AlarmEvent :
(
    Raised := 0,
    Cleared := 1,
    Acked := 2,
    Repeated := 3,
    Closed := 4
) USINT;
END_TYPE
