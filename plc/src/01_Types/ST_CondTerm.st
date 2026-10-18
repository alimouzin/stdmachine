// Terme de condition (docs/05 § 3.7)
{attribute 'pack_mode' := '1'}
TYPE ST_CondTerm :
STRUCT
    stLeft : ST_Value;
    eCmp : E_Cmp;
    stRight : ST_Value;
END_STRUCT
END_TYPE
