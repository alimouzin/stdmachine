// Heure UTC en unités de 100 ns depuis 1601 (FILETIME), comme les horodatages échangés avec la HMI
FUNCTION F_NowUtc : LINT
F_NowUtc := ULINT_TO_LINT(F_GetSystemTime());
