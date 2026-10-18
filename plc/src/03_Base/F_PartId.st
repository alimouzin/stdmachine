// Identifiant de pièce généré (docs/05 § 5.5) : <machine>-<AAAAMMJJ>-<compteur sur 6 chiffres>
FUNCTION F_PartId : STRING(40)
VAR_INPUT
    sMachine : STRING(40);
    nCounter : UDINT;
END_VAR
VAR
    stFt : T_FILETIME;
    stTime : TIMESTRUCT;
    nNow : ULINT;
    sDate : STRING(10);
    sNum : STRING(10);
END_VAR
nNow := F_GetSystemTime();
stFt.dwLowDateTime := ULINT_TO_DWORD(nNow AND 16#FFFFFFFF);
stFt.dwHighDateTime := ULINT_TO_DWORD(SHR(nNow, 32));
stTime := FILETIME_TO_SYSTEMTIME(stFt);
sDate := CONCAT(WORD_TO_STRING(stTime.wYear), CONCAT(F_Pad(WORD_TO_UDINT(stTime.wMonth), 2), F_Pad(WORD_TO_UDINT(stTime.wDay), 2)));
sNum := F_Pad(nCounter MOD 1000000, 6);
F_PartId := CONCAT(CONCAT(CONCAT(sMachine, '-'), sDate), CONCAT('-', sNum));
