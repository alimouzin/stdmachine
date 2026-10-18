// Durée du cycle de la tâche courante en ms (docs/04 § 2)
FUNCTION F_CycleMs : LREAL
VAR
    nTask : DINT;
END_VAR
nTask := GETCURTASKINDEXEX();
F_CycleMs := UDINT_TO_LREAL(TwinCAT_SystemInfoVarList._TaskInfo[nTask].CycleTime) / 10000.0;
