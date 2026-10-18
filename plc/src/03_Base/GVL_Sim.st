// Simulation de toute la machine (docs/04 § 6.3)
{attribute 'qualified_only'}
VAR_GLOBAL
    bAllSimulated : BOOL;
    nSeed : UDINT := 16#2545F491;      // générateur pseudo-aléatoire des objets simulés (F_SimRandom)
END_VAR
