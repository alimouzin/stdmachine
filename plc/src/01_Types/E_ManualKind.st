// Nature d'une demande du canal manuel (docs/05 § 7)
{attribute 'qualified_only'}
{attribute 'strict'}
TYPE E_ManualKind :
(
    Action := 0,
    JogStart := 1,
    JogStop := 2,
    Ack := 3,
    TeachPosition := 4
) USINT;
END_TYPE
