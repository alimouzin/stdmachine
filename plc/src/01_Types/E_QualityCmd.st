// Commande de la surveillance des rebuts (docs/10 § 10)
{attribute 'qualified_only'}
{attribute 'strict'}
TYPE E_QualityCmd :
(
    None := 0,
    Release := 1,
    Reset := 2
) USINT;
END_TYPE
