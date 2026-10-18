// Comparaison d'un terme de condition (docs/05 § 3.7)
{attribute 'qualified_only'}
{attribute 'strict'}
TYPE E_Cmp :
(
    Eq := 0,
    Ne := 1,
    Lt := 2,
    Le := 3,
    Gt := 4,
    Ge := 5,
    IsTrue := 6,
    IsFalse := 7
) USINT;
END_TYPE
