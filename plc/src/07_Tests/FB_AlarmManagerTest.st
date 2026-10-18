// Tests du gestionnaire d'alarmes (docs/04 § 10, docs/14 § 5)
FUNCTION_BLOCK FB_AlarmManagerTest EXTENDS TcUnit.FB_TestSuite
VAR
    stBuf : ST_AlarmBuffer;
END_VAR

RaiseTwiceOnce();
ClearedUnackedClosesOnAck();
LatchedEventClosesOnAck();
InfoClosesAtOnce();
AckedConditionClosesOnClear();

METHOD PRIVATE RaiseTwiceOnce
VAR
    fb : FB_AlarmManager;
END_VAR
TEST('RaiseTwiceOnce');
MEMSET(ADR(stBuf), 0, SIZEOF(stBuf));
fb.Bind(stBuf);
fb.Raise(1001, 101, 5, E_Severity.StationFault);
fb.Raise(1001, 101, 6, E_Severity.StationFault);
AssertEquals_UINT(Expected := 1, Actual := stBuf.nActive, Message := 'Une seule alarme pour la meme cle');
AssertEquals_UINT(Expected := 1, Actual := stBuf.aActive[1].nRepeat, Message := 'Repetition comptee');
AssertEquals_UDINT(Expected := 6, Actual := stBuf.aActive[1].nSubCode, Message := 'Sous-code mis a jour');
TEST_FINISHED();
END_METHOD

METHOD PRIVATE ClearedUnackedClosesOnAck
VAR
    fb : FB_AlarmManager;
END_VAR
TEST('ClearedUnackedClosesOnAck');
MEMSET(ADR(stBuf), 0, SIZEOF(stBuf));
fb.Bind(stBuf);
fb.Raise(2003, 100, 0, E_Severity.Warning);
fb.Clear(2003, 100);
AssertTrue(Condition := stBuf.nActive = 1 AND_THEN stBuf.aActive[1].eState = E_AlarmState.ClearedUnacked, Message := 'Disparue non acquittee : reste affichee');
fb.AckWarning(2003, 100, 'test');
AssertEquals_UINT(Expected := 0, Actual := stBuf.nActive, Message := 'Acquittee : close');
TEST_FINISHED();
END_METHOD

METHOD PRIVATE LatchedEventClosesOnAck
VAR
    fb : FB_AlarmManager;
END_VAR
TEST('LatchedEventClosesOnAck');
MEMSET(ADR(stBuf), 0, SIZEOF(stBuf));
fb.Bind(stBuf);
fb.RaiseEvent(1000, 901, 1, E_Severity.StationFault);
fb.AckFaults(1, FALSE, 'test');
AssertEquals_UINT(Expected := 0, Actual := stBuf.nActive, Message := 'Alarme sur evenement close a l acquittement');
TEST_FINISHED();
END_METHOD

METHOD PRIVATE InfoClosesAtOnce
VAR
    fb : FB_AlarmManager;
END_VAR
TEST('InfoClosesAtOnce');
MEMSET(ADR(stBuf), 0, SIZEOF(stBuf));
fb.Bind(stBuf);
fb.RaiseEvent(0, 916, 2, E_Severity.Info);
AssertEquals_UINT(Expected := 0, Actual := stBuf.nActive, Message := 'Information close des son apparition');
AssertEquals_UDINT(Expected := 2, Actual := stBuf.nWriteSeq, Message := 'Evenements apparition et cloture ecrits');
TEST_FINISHED();
END_METHOD

METHOD PRIVATE AckedConditionClosesOnClear
VAR
    fb : FB_AlarmManager;
END_VAR
TEST('AckedConditionClosesOnClear');
MEMSET(ADR(stBuf), 0, SIZEOF(stBuf));
fb.Bind(stBuf);
fb.Raise(3001, 100, 0, E_Severity.Warning);
fb.AckWarning(0, 0, 'test');
AssertTrue(Condition := stBuf.nActive = 1 AND_THEN stBuf.aActive[1].eState = E_AlarmState.ActiveAcked, Message := 'Acquittee, cause presente : reste active');
fb.Clear(3001, 100);
AssertEquals_UINT(Expected := 0, Actual := stBuf.nActive, Message := 'Cause disparue apres acquittement : close');
TEST_FINISHED();
END_METHOD
END_FUNCTION_BLOCK
