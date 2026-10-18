// Nature d'une valeur rémanente sauvegardée par la HMI (docs/02 § 7.4)
{attribute 'qualified_only'}
{attribute 'strict'}
TYPE E_RetainedKind :
(
    None := 0,
    StationVar := 1,       // variable persistante (calibration)
    MoverVar := 2,         // variable de mover
    ObjectCounters := 3,   // compteurs de maintenance d'un objet
    PlantCounters := 4,    // compteurs de production et de temps
    Calibration := 5       // état et date d'une calibration
) USINT;
END_TYPE
