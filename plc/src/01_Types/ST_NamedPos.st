// Position nommée (docs/06 § 2)
{attribute 'pack_mode' := '1'}
TYPE ST_NamedPos :
STRUCT
    stPos : ST_Position;
    fApproach : LREAL;
    bTaught : BOOL;
END_STRUCT
END_TYPE
