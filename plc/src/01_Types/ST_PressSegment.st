// Segment d'une recette de presse (docs/06 § 4)
{attribute 'pack_mode' := '1'}
TYPE ST_PressSegment :
STRUCT
    eTarget : USINT;            // 0 course, 1 force
    fTarget : LREAL;
    fSpeedPct : LREAL;
    fForceMin : LREAL;
    fForceMax : LREAL;
    fStrokeMin : LREAL;
    fStrokeMax : LREAL;
END_STRUCT
END_TYPE
