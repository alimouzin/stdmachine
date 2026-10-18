// Forcer une entrée d'un objet simulé (docs/04 § 6.3), Maintenance
{attribute 'pack_mode' := '1'}
TYPE ST_SimForceRequest :
STRUCT
    nSeq : UDINT;
    nStation : USINT;
    nObject : UINT;
    nSignal : USINT;                // S1..S8
    fValue : LREAL;
    bRelease : BOOL;                // faux = forcer, vrai = rendre la main à la simulation
END_STRUCT
END_TYPE
