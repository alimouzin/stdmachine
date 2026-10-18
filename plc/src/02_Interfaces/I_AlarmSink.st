// Réception des alarmes (docs/04 § 4, docs/14)
INTERFACE I_AlarmSink
METHOD Raise
VAR_INPUT
    nGlobalId : UDINT;
    nCode : UINT;
    nSubCode : UDINT;
    eSeverity : E_Severity;
END_VAR
END_METHOD
METHOD Clear
VAR_INPUT
    nGlobalId : UDINT;
    nCode : UINT;
END_VAR
END_METHOD
END_INTERFACE
