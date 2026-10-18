// État d'une alarme active (docs/14 § 5)
{attribute 'qualified_only'}
{attribute 'strict'}
TYPE E_AlarmState :
(
    ActiveUnacked := 0,
    ActiveAcked := 1,
    ClearedUnacked := 2
) USINT;
END_TYPE
