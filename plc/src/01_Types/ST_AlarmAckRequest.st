// Acquitter un avertissement (docs/06 § 6.2) ; nGlobalId 0 = tous les avertissements
{attribute 'pack_mode' := '1'}
TYPE ST_AlarmAckRequest :
STRUCT
    nSeq : UDINT;
    nGlobalId : UDINT;
    nCode : UINT;
    sUser : STRING(24);
END_STRUCT
END_TYPE
