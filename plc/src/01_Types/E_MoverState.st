// État d'un mover dans le séquenceur général (docs/05 § 5.3)
{attribute 'qualified_only'}
{attribute 'strict'}
TYPE E_MoverState :
(
    Unknown := 0,
    Ready := 1,
    Routing := 2,
    Queued := 3,
    AtStation := 4,
    Buffered := 5,
    ToEmpty := 6,
    Error := 7,
    Halted := 8,
    OutOfService := 9
) USINT;
END_TYPE
