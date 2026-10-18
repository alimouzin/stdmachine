// Statut d'un contexte d'exécution (docs/05 § 3.1)
{attribute 'qualified_only'}
{attribute 'strict'}
TYPE E_CtxStatus :
(
    Free := 0,
    Running := 1,
    WaitAction := 2,
    WaitCond := 3,
    WaitTime := 4,
    WaitJoin := 5,
    WaitStep := 6,
    Holding := 7,
    Done := 8,
    Failed := 9
) USINT;
END_TYPE
