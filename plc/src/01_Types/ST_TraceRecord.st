// Trace d'une opération sur une pièce (docs/05 § 9)
{attribute 'pack_mode' := '1'}
TYPE ST_TraceRecord :
STRUCT
    nSeq : UDINT;
    sPartId : STRING(40);
    nOrder : UDINT;
    nMover : USINT;
    nSlot : USINT;
    nStation : USINT;
    nGroup : USINT;
    nOperation : USINT;
    tStart : LINT;
    tEnd : LINT;
    eVerdict : E_Verdict;
    nErrorCode : UINT;
    aData : ARRAY[1..GVL_Const.MAX_PART_DATA] OF LREAL;
    nCurveId : UDINT;               // 0 = aucune
END_STRUCT
END_TYPE
