// Commande d'OF (docs/05 § 5.6)
{attribute 'qualified_only'}
{attribute 'strict'}
TYPE E_OrderCmd :
(
    None := 0,
    Activate := 1,
    Suspend := 2,
    Cancel := 3
) USINT;
END_TYPE
