// Tests des fonctions de base : CRC-32, comparaisons, opérations de SetVar, verdicts, états de calibration
FUNCTION_BLOCK FB_HelpersTest EXTENDS TcUnit.FB_TestSuite
VAR
END_VAR

Crc32Reference();
Crc32Incremental();
CompareTerms();
ApplyOperations();
VerdictOfData();
CalibStateRoundTrip();

METHOD PRIVATE Crc32Reference
VAR
    fb : FB_Crc32;
    s : STRING(20) := '123456789';
END_VAR
TEST('Crc32Reference');
fb.Begin(ADR(s), 9);
AssertTrue(Condition := fb.Step(1000), Message := 'Calcul termine en un pas');
AssertEquals_UDINT(Expected := 16#CBF43926, Actual := fb.Value, Message := 'CRC-32 IEEE de 123456789');
TEST_FINISHED();
END_METHOD

METHOD PRIVATE Crc32Incremental
VAR
    fb : FB_Crc32;
    s : STRING(20) := '123456789';
    n : INT;
END_VAR
TEST('Crc32Incremental');
fb.Begin(ADR(s), 9);
WHILE NOT fb.Step(2) AND n < 10 DO n := n + 1; END_WHILE
AssertEquals_INT(Expected := 4, Actual := n, Message := 'Cinq tranches de deux octets au plus');
AssertEquals_UDINT(Expected := 16#CBF43926, Actual := fb.Value, Message := 'Meme CRC par tranches');
TEST_FINISHED();
END_METHOD

METHOD PRIVATE CompareTerms
TEST('CompareTerms');
AssertTrue(Condition := F_Compare(1.0, E_Cmp.Eq, 1.0), Message := 'Eq');
AssertTrue(Condition := F_Compare(1.0, E_Cmp.Ne, 2.0), Message := 'Ne');
AssertTrue(Condition := F_Compare(1.0, E_Cmp.Lt, 2.0), Message := 'Lt');
AssertTrue(Condition := F_Compare(2.0, E_Cmp.Le, 2.0), Message := 'Le egal');
AssertFalse(Condition := F_Compare(2.0, E_Cmp.Gt, 2.0), Message := 'Gt strict');
AssertTrue(Condition := F_Compare(2.0, E_Cmp.Ge, 2.0), Message := 'Ge egal');
AssertTrue(Condition := F_Compare(1.0, E_Cmp.IsTrue, 0.0), Message := 'IsTrue');
AssertTrue(Condition := F_Compare(0.0, E_Cmp.IsFalse, 0.0), Message := 'IsFalse');
TEST_FINISHED();
END_METHOD

METHOD PRIVATE ApplyOperations
TEST('ApplyOperations');
AssertEquals_LREAL(Expected := 2.0, Actual := F_ApplyOp(5.0, 2.0, 0), Delta := 1.0E-9, Message := 'Affecter');
AssertEquals_LREAL(Expected := 7.0, Actual := F_ApplyOp(5.0, 2.0, 1), Delta := 1.0E-9, Message := 'Ajouter');
AssertEquals_LREAL(Expected := 3.0, Actual := F_ApplyOp(5.0, 2.0, 2), Delta := 1.0E-9, Message := 'Soustraire');
TEST_FINISHED();
END_METHOD

METHOD PRIVATE VerdictOfData
TEST('VerdictOfData');
AssertTrue(Condition := F_Verdict(0.0) = E_Verdict.Undefined, Message := '0 : non defini');
AssertTrue(Condition := F_Verdict(1.0) = E_Verdict.Good, Message := '1 : bon');
AssertTrue(Condition := F_Verdict(2.0) = E_Verdict.Bad, Message := '2 : mauvais');
TEST_FINISHED();
END_METHOD

METHOD PRIVATE CalibStateRoundTrip
TEST('CalibStateRoundTrip');
AssertTrue(Condition := F_CalibStateOf(F_CalibStateNum(E_CalibState.Valid)) = E_CalibState.Valid, Message := 'Valid');
AssertTrue(Condition := F_CalibStateOf(F_CalibStateNum(E_CalibState.Expired)) = E_CalibState.Expired, Message := 'Expired');
AssertTrue(Condition := F_CalibStateOf(F_CalibStateNum(E_CalibState.Failed)) = E_CalibState.Failed, Message := 'Failed');
AssertTrue(Condition := F_CalibStateOf(F_CalibStateNum(E_CalibState.Running)) = E_CalibState.Never, Message := 'Running restaure en Never');
TEST_FINISHED();
END_METHOD
END_FUNCTION_BLOCK
