// États d'un objet (docs/04 § 5 et § 7.1)
{attribute 'qualified_only'}
{attribute 'strict'}
TYPE E_ObjState :
(
    NotInitialized := 0,
    Ready := 1,
    Busy := 2,
    Error := 3,
    Disabled := 4
) USINT;
END_TYPE
