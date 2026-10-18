// Toujours X, Y, Z, C + frame (docs/04 § 5). Le frame n'est exploité que par un robot Stäubli (D86).
{attribute 'pack_mode' := '1'}
TYPE ST_Position :
STRUCT
    fX : LREAL;                 // mm
    fY : LREAL;                 // mm
    fZ : LREAL;                 // mm
    fC : LREAL;                 // degrés
    nFrame : INT;               // 0 = repère machine ou repère des axes
END_STRUCT
END_TYPE
