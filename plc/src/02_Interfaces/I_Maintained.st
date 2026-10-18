// Compteurs de maintenance persistants d'un élément de pool (docs/11 § 5, D92) et leur restauration (docs/02 § 7.4)
INTERFACE I_Maintained
// Objet du projet auquel appartiennent les compteurs (persistant : survit au redémarrage du PLC)
PROPERTY StableId : UDINT
GET
END_GET
END_PROPERTY
PROPERTY ActionCount : UDINT
GET
END_GET
END_PROPERTY
PROPERTY ErrorCount : UDINT
GET
END_GET
END_PROPERTY
PROPERTY MotionHours : LREAL
GET
END_GET
END_PROPERTY
METHOD RestoreCounters
VAR_INPUT
    nActions : UDINT;
    nErrors : UDINT;
    fHours : LREAL;
END_VAR
END_METHOD
END_INTERFACE
