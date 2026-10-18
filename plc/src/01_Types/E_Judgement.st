// Jugement d'un résultat (docs/04 § 5)
{attribute 'qualified_only'}
{attribute 'strict'}
TYPE E_Judgement :
(
    None := 0,
    Ok := 1,
    Nok := 2,
    High := 3,
    Low := 4
) USINT;
END_TYPE
