// Source d'une valeur dans une instruction (docs/06 § 2)
{attribute 'qualified_only'}
{attribute 'strict'}
TYPE E_ValueSource :
(
    None := 0,
    Const := 1,
    Var := 2,
    PartData := 3,
    OpParam := 4,
    NamedPos := 5,
    Speed := 6,
    Text := 7,
    ObjValue := 8,
    System := 9,
    MoverData := 10,
    OrderParam := 11
) USINT;
END_TYPE
