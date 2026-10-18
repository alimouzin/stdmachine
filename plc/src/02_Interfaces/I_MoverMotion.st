// Mouvement d'un mover hors process (mode manuel, docs/05 § 6.5) : le gestionnaire de movers tient l'occupation
// à jour. Donnée aux objets Mover par GVL_IoRef.ipMovers.
INTERFACE I_MoverMotion
// Déplacement vers une position quelconque du plan ; fSpeedPct en % de la vitesse par défaut des movers
METHOD MoveFree : BOOL
VAR_INPUT
    nMover : USINT;
    stPos : ST_Position;
    fSpeedPct : LREAL;
END_VAR
END_METHOD
METHOD StopMover
VAR_INPUT
    nMover : USINT;
END_VAR
END_METHOD
METHOD Moving : BOOL
VAR_INPUT
    nMover : USINT;
END_VAR
END_METHOD
METHOD Position : ST_Position
VAR_INPUT
    nMover : USINT;
END_VAR
END_METHOD
END_INTERFACE
