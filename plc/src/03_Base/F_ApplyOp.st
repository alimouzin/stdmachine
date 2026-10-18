// SetVar : 0 affecter, 1 ajouter, 2 soustraire (docs/06 § 3)
FUNCTION F_ApplyOp : LREAL
VAR_INPUT
    fCurrent : LREAL;
    fValue : LREAL;
    nOp : DINT;
END_VAR
CASE nOp OF
    1: F_ApplyOp := fCurrent + fValue;
    2: F_ApplyOp := fCurrent - fValue;
ELSE
    F_ApplyOp := fValue;
END_CASE
