// Opération subie par une pièce (docs/05 § 9)
{attribute 'pack_mode' := '1'}
TYPE ST_PartOp :
STRUCT
    nStation : USINT;
    nGroup : USINT;
    eVerdictAfter : E_Verdict;
END_STRUCT
END_TYPE
