// Commandes d'état (docs/02 § 8). DrainStart et DrainCancel ne concernent que la machine (docs/05 § 5.7) :
// FB_PackML les ignore, le séquenceur général les traite.
{attribute 'qualified_only'}
{attribute 'strict'}
TYPE E_Cmd :
(
    None := 0,
    Reset := 1,
    Start := 2,
    Stop := 3,
    Hold := 4,
    Abort := 5,
    Clear := 6,
    DrainStart := 7,
    DrainCancel := 8
) USINT;
END_TYPE
