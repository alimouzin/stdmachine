// Règle de remise à zéro d'une variable de mover (docs/05 § 5.8)
{attribute 'qualified_only'}
{attribute 'strict'}
TYPE E_MoverVarReset :
(
    Never := 0,
    OnDeploy := 1,
    OnEmpty := 2
) USINT;
END_TYPE
