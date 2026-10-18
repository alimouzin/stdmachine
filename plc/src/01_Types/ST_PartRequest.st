// Confirmer le retrait des pièces d'un mover à vider (docs/06 § 6.2)
{attribute 'pack_mode' := '1'}
TYPE ST_PartRequest :
STRUCT
    nSeq : UDINT;
    nMover : USINT;
    sUser : STRING(24);
END_STRUCT
END_TYPE
