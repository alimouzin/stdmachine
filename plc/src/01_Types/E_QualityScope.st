// Portée de la surveillance des rebuts (docs/10 § 9)
{attribute 'qualified_only'}
{attribute 'strict'}
TYPE E_QualityScope :
(
    Station := 0,
    StationImputed := 1,
    Group := 2,
    Mover := 3,
    Machine := 4,
    Order := 5
) USINT;
END_TYPE
