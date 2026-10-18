// Pilote du système planaire (docs/05 § 6). Le séquenceur ne donne que des destinations ; trajets et évitement
// de collision sont faits par le système planaire. FB_PlanarSim l'implémente en simulation ; le pilote Tc3 Planar
// (projet de la machine ou bibliothèque à venir) implémente la même interface.
INTERFACE I_PlanarDriver
METHOD Activate : BOOL              // TRUE quand le système est actif
VAR_INPUT
    bEnable : BOOL;
END_VAR
END_METHOD
METHOD MoveTo : BOOL                // TRUE si la commande est acceptée
VAR_INPUT
    nMover : USINT;
    stPos : ST_Position;
    fVelocity : LREAL;               // mm/s, override déjà appliqué par le pilote
    fAcceleration : LREAL;
END_VAR
END_METHOD
METHOD StopAll
END_METHOD
METHOD StopMover                    // arrêt contrôlé d'un mover (mode manuel)
VAR_INPUT
    nMover : USINT;
END_VAR
END_METHOD
METHOD AckError
END_METHOD
METHOD Cycle
VAR_INPUT
    fOverride : LREAL;               // override machine effectif, %
END_VAR
END_METHOD
METHOD MoverPosition : ST_Position
VAR_INPUT
    nMover : USINT;
END_VAR
END_METHOD
METHOD MoverMoving : BOOL
VAR_INPUT
    nMover : USINT;
END_VAR
END_METHOD
METHOD MoverDetected : BOOL
VAR_INPUT
    nMover : USINT;
END_VAR
END_METHOD
METHOD SetInitialPosition           // simulation : poser un mover sur le plan
VAR_INPUT
    nMover : USINT;
    stPos : ST_Position;
END_VAR
END_METHOD
PROPERTY Active : BOOL
GET
END_GET
END_PROPERTY
PROPERTY ErrorCode : UINT          // 0 ou erreur du type Système planaire (100 collision, 101 mover non détecté, 102 groupe)
GET
END_GET
END_PROPERTY
PROPERTY ErrorSubCode : UDINT
GET
END_GET
END_PROPERTY
END_INTERFACE
