// Nature d'un forçage (docs/12 § 7)
{attribute 'qualified_only'}
{attribute 'strict'}
TYPE E_ForceKind :
(
    None := 0,
    DeclaredState := 1,
    StationOutOfProduction := 2,
    MoverOutOfService := 3
) USINT;
END_TYPE
