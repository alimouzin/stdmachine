// Nature d'un paramètre d'action résolu (docs/04 § 5)
{attribute 'qualified_only'}
{attribute 'strict'}
TYPE E_ParamKind :
(
    Unused := 0,
    Real := 1,
    Position := 2,
    SpeedIndex := 3,
    SpeedPercent := 4,
    Text := 5
) USINT;
END_TYPE
