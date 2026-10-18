// Réponse d'une station à une demande d'opération (docs/05 § 4.1)
{attribute 'qualified_only'}
{attribute 'strict'}
TYPE E_OpStatus :
(
    Idle := 0,
    Running := 1,
    Done := 2,
    Failed := 3,
    Aborted := 4,
    Preparing := 5
) USINT;
END_TYPE
