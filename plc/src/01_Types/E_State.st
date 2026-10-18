// États PackML réduits (docs/02 § 8, docs/05 § 2)
{attribute 'qualified_only'}
{attribute 'strict'}
TYPE E_State :
(
    Stopped := 0,
    Resetting := 1,
    Idle := 2,
    Execute := 3,
    Held := 4,
    Stopping := 5,
    Aborted := 6,
    Clearing := 7
) USINT;
END_TYPE
