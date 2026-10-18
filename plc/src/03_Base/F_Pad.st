// Nombre écrit sur nWidth chiffres, complété par des zéros
FUNCTION F_Pad : STRING(10)
VAR_INPUT
    nValue : UDINT;
    nWidth : INT;
END_VAR
F_Pad := UDINT_TO_STRING(nValue);
WHILE LEN(F_Pad) < nWidth DO
    F_Pad := CONCAT('0', F_Pad);
END_WHILE
