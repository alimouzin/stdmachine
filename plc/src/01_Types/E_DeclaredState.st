// État déclaré de la machine (docs/12 § 2)
{attribute 'qualified_only'}
{attribute 'strict'}
TYPE E_DeclaredState :
(
    None := 0,
    PlannedStop := 1,
    Changeover := 2,
    WaitUpstream := 3,
    WaitDownstream := 4,
    PlannedMaintenance := 5,
    CorrectiveMaintenance := 6,
    Trials := 7,
    Cleaning := 8
) USINT;
END_TYPE
