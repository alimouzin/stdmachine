// Commande de déploiement (docs/05 § 10)
{attribute 'qualified_only'}
{attribute 'strict'}
TYPE E_DeployCmd :
(
    None := 0,
    Verify := 1,
    Activate := 2,
    Cancel := 3
) USINT;
END_TYPE
