// Motif d'attente d'un mover en tampon (docs/05 § 5.4, 5.6, 5.7, docs/10)
{attribute 'qualified_only'}
{attribute 'strict'}
TYPE E_WaitReason :
(
    None := 0,
    NoStation := 1,
    OrderComplete := 2,
    NoOrder := 3,
    QualityStop := 4,
    Drain := 5,
    Held := 6
) USINT;
END_TYPE
