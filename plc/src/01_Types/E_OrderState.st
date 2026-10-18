// État d'un OF dans la table du PLC (docs/05 § 5.6)
{attribute 'qualified_only'}
{attribute 'strict'}
TYPE E_OrderState :
(
    Free := 0,
    Active := 1,
    Suspended := 2,
    Done := 3
) USINT;
END_TYPE
