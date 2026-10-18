// Tampon circulaire de traces lu par la HMI depuis le dernier nSeq reçu (docs/05 § 9)
FUNCTION_BLOCK FB_TraceBuffer
VAR
    _refBuf : REFERENCE TO ST_TraceBuffer;
END_VAR

METHOD Bind
VAR_INPUT
    refBuf : REFERENCE TO ST_TraceBuffer;
END_VAR
_refBuf REF= refBuf;
END_METHOD

METHOD Add : UDINT
VAR_INPUT
    stTrace : ST_TraceRecord;
END_VAR
VAR
    k : INT;
END_VAR
IF NOT __ISVALIDREF(_refBuf) THEN RETURN; END_IF
_refBuf.nWriteSeq := _refBuf.nWriteSeq + 1;
k := UDINT_TO_INT(_refBuf.nWriteSeq MOD INT_TO_UDINT(GVL_Const.TRACE_BUFFER));
_refBuf.aTrace[k] := stTrace;
_refBuf.aTrace[k].nSeq := _refBuf.nWriteSeq;
Add := _refBuf.nWriteSeq;
END_METHOD
END_FUNCTION_BLOCK
