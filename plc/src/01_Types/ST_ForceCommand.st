// Poser ou lever un forçage (docs/12 § 7)
{attribute 'pack_mode' := '1'}
TYPE ST_ForceCommand :
STRUCT
    nSeq : UDINT;
    eKind : E_ForceKind;
    bSet : BOOL;                    // faux = lever
    nTarget : USINT;                // station ou mover
    eDeclared : E_DeclaredState;
    bStartBlocked : BOOL;
    bImmediate : BOOL;              // mover hors service immédiatement
    nReason : USINT;                // code de motif
    tUntil : LINT;                  // échéance, 0 = aucune (gérée par la HMI)
    sUser : STRING(24);
END_STRUCT
END_TYPE
