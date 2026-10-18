// Nature d'une table (docs/06 § 2)
{attribute 'qualified_only'}
{attribute 'strict'}
TYPE E_TableKind :
(
    Operation := 0,
    Background := 1,
    Init := 2,
    Stop := 3,
    Clear := 4,
    Sub := 5,
    Calibration := 6,
    General := 10,
    GeneralSub := 11,
    MachineCalibration := 12
) USINT;
END_TYPE
