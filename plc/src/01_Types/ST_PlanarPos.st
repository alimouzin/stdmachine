// Position planaire (docs/05 § 6.1), toujours en frame 0
{attribute 'pack_mode' := '1'}
TYPE ST_PlanarPos :
STRUCT
    stPos : ST_Position;
    eKind : E_PlanarPosKind;
    nStation : USINT;
END_STRUCT
END_TYPE
