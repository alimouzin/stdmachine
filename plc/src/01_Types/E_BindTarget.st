// Cible d'une liaison de résultat ou d'un SetVar (docs/06 § 2)
{attribute 'qualified_only'}
{attribute 'strict'}
TYPE E_BindTarget :
(
    None := 0,
    Var := 1,
    PartData := 2,
    PartId := 3,
    Verdict := 4,
    PartRemoved := 5,
    MoverData := 6,
    Output := 7
) USINT;
END_TYPE
