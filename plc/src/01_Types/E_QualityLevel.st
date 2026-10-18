// Niveau d'une portée qualité (docs/10 § 9)
{attribute 'qualified_only'}
{attribute 'strict'}
TYPE E_QualityLevel :
(
    Normal := 0,
    Warning := 1,
    Action := 2,
    Probation := 3
) USINT;
END_TYPE
