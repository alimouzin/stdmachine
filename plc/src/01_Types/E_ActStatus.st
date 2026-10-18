// Statut de l'action d'un objet (docs/04 § 5)
{attribute 'qualified_only'}
{attribute 'strict'}
TYPE E_ActStatus :
(
    Idle := 0,
    Busy := 1,
    Done := 2,
    Error := 3
) USINT;
END_TYPE
