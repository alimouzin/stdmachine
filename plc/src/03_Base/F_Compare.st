// Terme de condition (docs/05 § 3.7) : égalités sur réels à 1e-9 près
FUNCTION F_Compare : BOOL
VAR_INPUT
    a : LREAL;
    eCmp : E_Cmp;
    b : LREAL;
END_VAR
CASE eCmp OF
    E_Cmp.Eq: F_Compare := ABS(a - b) < 1.0E-9;
    E_Cmp.Ne: F_Compare := ABS(a - b) >= 1.0E-9;
    E_Cmp.Lt: F_Compare := a < b;
    E_Cmp.Le: F_Compare := a <= b + 1.0E-9;
    E_Cmp.Gt: F_Compare := a > b;
    E_Cmp.Ge: F_Compare := a >= b - 1.0E-9;
    E_Cmp.IsTrue: F_Compare := a <> 0;
    E_Cmp.IsFalse: F_Compare := a = 0;
ELSE
    F_Compare := FALSE;
END_CASE
