// Verdict d'une pièce depuis sa donnée 1 (0 non défini, 1 bon, 2 mauvais)
FUNCTION F_Verdict : E_Verdict
VAR_INPUT
    fData : LREAL;
END_VAR
IF fData > 1.5 THEN
    F_Verdict := E_Verdict.Bad;
ELSIF fData > 0.5 THEN
    F_Verdict := E_Verdict.Good;
ELSE
    F_Verdict := E_Verdict.Undefined;
END_IF
END_FUNCTION
