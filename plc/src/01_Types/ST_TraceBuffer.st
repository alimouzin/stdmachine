// Tampon de traces (docs/05 § 9)
{attribute 'pack_mode' := '1'}
TYPE ST_TraceBuffer :
STRUCT
    nWriteSeq : UDINT;
    aTrace : ARRAY[0..GVL_Const.TRACE_BUFFER - 1] OF ST_TraceRecord;
END_STRUCT
END_TYPE
