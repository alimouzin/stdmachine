// État d'une pièce (docs/05 § 9)
{attribute 'qualified_only'}
{attribute 'strict'}
TYPE E_PartStatus :
(
    None := 0,
    InProcess := 1,
    Good := 2,
    Bad := 3,
    Exited := 4,
    Rejected := 5,
    Removed := 6
) USINT;
END_TYPE
