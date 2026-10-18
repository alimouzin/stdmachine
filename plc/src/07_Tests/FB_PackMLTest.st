// Tests de la machine d'états PackML réduite (docs/05 § 2)
FUNCTION_BLOCK FB_PackMLTest EXTENDS TcUnit.FB_TestSuite
VAR
END_VAR

ResetThenIdle();
StartHoldRestart();
StopCompletes();
FaultAborts();
ClearReturnsStopped();
RejectedCommands();

METHOD PRIVATE ResetThenIdle
VAR
    fb : FB_PackML;
END_VAR
TEST('ResetThenIdle');
fb(eCmd := E_Cmd.Reset, bStateComplete := FALSE, bFault := FALSE);
AssertTrue(Condition := fb.eState = E_State.Resetting AND fb.bEntry, Message := 'Reset depuis Stopped : Resetting');
fb(eCmd := E_Cmd.None, bStateComplete := FALSE, bFault := FALSE);
AssertTrue(Condition := fb.eState = E_State.Resetting AND NOT fb.bEntry, Message := 'Resetting tant que le travail n est pas fini');
fb(eCmd := E_Cmd.None, bStateComplete := TRUE, bFault := FALSE);
AssertTrue(Condition := fb.eState = E_State.Idle, Message := 'Resetting termine : Idle');
TEST_FINISHED();
END_METHOD

METHOD PRIVATE StartHoldRestart
VAR
    fb : FB_PackML;
END_VAR
TEST('StartHoldRestart');
fb(eCmd := E_Cmd.Reset);
fb(eCmd := E_Cmd.None, bStateComplete := TRUE);
fb(eCmd := E_Cmd.Start, bStateComplete := FALSE);
AssertTrue(Condition := fb.eState = E_State.Execute, Message := 'Start depuis Idle : Execute');
fb(eCmd := E_Cmd.Hold);
AssertTrue(Condition := fb.eState = E_State.Held, Message := 'Hold : Held');
fb(eCmd := E_Cmd.Start);
AssertTrue(Condition := fb.eState = E_State.Execute, Message := 'Start depuis Held : Execute');
TEST_FINISHED();
END_METHOD

METHOD PRIVATE StopCompletes
VAR
    fb : FB_PackML;
END_VAR
TEST('StopCompletes');
fb(eCmd := E_Cmd.Reset);
fb(eCmd := E_Cmd.None, bStateComplete := TRUE);
fb(eCmd := E_Cmd.Start, bStateComplete := FALSE);
fb(eCmd := E_Cmd.Stop);
AssertTrue(Condition := fb.eState = E_State.Stopping, Message := 'Stop : Stopping');
fb(eCmd := E_Cmd.None, bStateComplete := TRUE);
AssertTrue(Condition := fb.eState = E_State.Stopped, Message := 'Stopping termine : Stopped');
TEST_FINISHED();
END_METHOD

METHOD PRIVATE FaultAborts
VAR
    fb : FB_PackML;
END_VAR
TEST('FaultAborts');
fb(eCmd := E_Cmd.Reset);
fb(eCmd := E_Cmd.None, bStateComplete := TRUE);
fb(eCmd := E_Cmd.Start, bStateComplete := FALSE);
fb(eCmd := E_Cmd.None, bFault := TRUE);
AssertTrue(Condition := fb.eState = E_State.Aborted, Message := 'Defaut en Execute : Aborted');
fb(eCmd := E_Cmd.Start, bFault := FALSE);
AssertTrue(Condition := fb.eState = E_State.Aborted, Message := 'Start refuse en Aborted');
TEST_FINISHED();
END_METHOD

METHOD PRIVATE ClearReturnsStopped
VAR
    fb : FB_PackML;
END_VAR
TEST('ClearReturnsStopped');
fb(eCmd := E_Cmd.Abort);
AssertTrue(Condition := fb.eState = E_State.Aborted, Message := 'Abort depuis Stopped : Aborted');
fb(eCmd := E_Cmd.Clear);
AssertTrue(Condition := fb.eState = E_State.Clearing, Message := 'Clear : Clearing');
fb(eCmd := E_Cmd.None, bStateComplete := TRUE);
AssertTrue(Condition := fb.eState = E_State.Stopped, Message := 'Clearing termine : Stopped');
TEST_FINISHED();
END_METHOD

METHOD PRIVATE RejectedCommands
VAR
    fb : FB_PackML;
END_VAR
TEST('RejectedCommands');
AssertFalse(Condition := fb.Accepts(E_Cmd.Start), Message := 'Start refuse en Stopped');
AssertFalse(Condition := fb.Accepts(E_Cmd.Hold), Message := 'Hold refuse en Stopped');
AssertTrue(Condition := fb.Accepts(E_Cmd.Reset), Message := 'Reset accepte en Stopped');
fb(eCmd := E_Cmd.Start);
AssertTrue(Condition := fb.eState = E_State.Stopped, Message := 'Start sans effet en Stopped');
TEST_FINISHED();
END_METHOD
END_FUNCTION_BLOCK
