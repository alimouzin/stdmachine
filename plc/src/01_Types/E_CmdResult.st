// Résultat d'une commande (docs/05 § 2, docs/06 § 6.1)
{attribute 'qualified_only'}
{attribute 'strict'}
TYPE E_CmdResult :
(
    None := 0,
    Accepted := 1,
    Rejected := 2
) USINT;
END_TYPE
