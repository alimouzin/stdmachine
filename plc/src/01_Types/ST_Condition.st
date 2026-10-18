// Condition (docs/05 § 3.7) ; une condition sans terme est vraie
{attribute 'pack_mode' := '1'}
TYPE ST_Condition :
STRUCT
    eCombine : E_Combine;
    nTerms : USINT;
    aTerm : ARRAY[1..GVL_Const.MAX_COND_TERMS] OF ST_CondTerm;
END_STRUCT
END_TYPE
