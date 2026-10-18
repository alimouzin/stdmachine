// Jeu d'instructions (docs/06 § 2) ; version 2 du jeu : 17 à 23
{attribute 'qualified_only'}
{attribute 'strict'}
TYPE E_Op :
(
    Nop := 0,
    Action := 1,
    WaitCond := 2,
    WaitTime := 3,
    Jump := 4,
    JumpIfFalse := 5,
    Fork := 6,
    Join := 7,
    LoopInit := 8,
    LoopNext := 9,
    Call := 10,
    Return := 11,
    SetVar := 12,
    Alarm := 13,
    End := 14,
    StepBegin := 15,
    StepEnd := 16,
    WaitMover := 17,
    Prompt := 18,
    SavePos := 19,
    Route := 20,
    CalibStation := 21,
    ResetMoverVars := 22,
    SetOut := 23
) UINT;
END_TYPE
