// Gravité d'une alarme (docs/04 § 5, docs/14 § 4)
{attribute 'qualified_only'}
{attribute 'strict'}
TYPE E_Severity :
(
    Info := 0,
    Warning := 1,
    StationFault := 2,
    MachineFault := 3,
    Quality := 4
) USINT;
END_TYPE
