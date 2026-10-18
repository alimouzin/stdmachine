// Surveillance des rebuts (docs/10 § 9) : rebuts successifs et taux sur fenêtre glissante en nombre de résultats,
// par station (constatés et imputés), fonction, mover, machine, et budget de rebuts par OF.
// Les réactions sont rendues par les sorties ; le séquenceur général les applique.
FUNCTION_BLOCK FB_QualityMonitor
VAR
    _refMachine : REFERENCE TO ST_MachineData;
    _refAlarms : REFERENCE TO FB_AlarmManager;
    aStationHold : ARRAY[1..GVL_Const.MAX_STATIONS] OF BOOL;   // réaction « arrêt qualité de la station »
    aMoverOut : ARRAY[1..GVL_Const.MAX_MOVERS] OF BOOL;        // réaction « mover hors service »
    bLoadingLocked : BOOL;                                     // réaction « arrêt du chargement »
    bMachineStop : BOOL;                                       // impulsion : réaction « arrêter la machine »
    bAnyReaction : BOOL;                                       // balise bleu clignotant
END_VAR
VAR PERSISTENT
    stTracks : ST_QualityStatus;
END_VAR

METHOD Init
VAR_INPUT
    refMachine : REFERENCE TO ST_MachineData;
    refAlarms : REFERENCE TO FB_AlarmManager;
END_VAR
_refMachine REF= refMachine;
_refAlarms REF= refAlarms;
END_METHOD

METHOD PRIVATE _Rule : ST_QualityRule
VAR_INPUT
    eScope : E_QualityScope;
    nIndex : INT;
END_VAR
_Rule := _refMachine.stQuality.aDefault[TO_INT(eScope)];
CASE eScope OF
    E_QualityScope.Station:
        IF nIndex >= 1 AND nIndex <= GVL_Const.MAX_STATIONS AND_THEN NOT _refMachine.stQuality.aStationInherits[nIndex] THEN
            _Rule := _refMachine.stQuality.aStationRule[nIndex];
        END_IF
    E_QualityScope.Group:
        IF nIndex >= 1 AND nIndex <= GVL_Const.MAX_GROUPS AND_THEN NOT _refMachine.stQuality.aGroupInherits[nIndex] THEN
            _Rule := _refMachine.stQuality.aGroupRule[nIndex];
        END_IF
END_CASE
END_METHOD

METHOD PRIVATE _Track : POINTER TO ST_QualityTrack
VAR_INPUT
    eScope : E_QualityScope;
    nIndex : INT;
END_VAR
CASE eScope OF
    E_QualityScope.Station: _Track := ADR(stTracks.aStation[LIMIT(1, nIndex, GVL_Const.MAX_STATIONS)]);
    E_QualityScope.StationImputed: _Track := ADR(stTracks.aStationImputed[LIMIT(1, nIndex, GVL_Const.MAX_STATIONS)]);
    E_QualityScope.Group: _Track := ADR(stTracks.aGroup[LIMIT(1, nIndex, GVL_Const.MAX_GROUPS)]);
    E_QualityScope.Mover: _Track := ADR(stTracks.aMover[LIMIT(1, nIndex, GVL_Const.MAX_MOVERS)]);
ELSE
    _Track := ADR(stTracks.stMachine);
END_CASE
END_METHOD

// Un résultat (docs/10 § 9, « À chaque résultat »)
METHOD Record
VAR_INPUT
    eScope : E_QualityScope;
    nIndex : INT;
    bBad : BOOL;
END_VAR
VAR
    t : REFERENCE TO ST_QualityTrack;
    r : ST_QualityRule;
    nOld : USINT;
    nConsecAct : USINT;
    fRate : LREAL;
    bActConsec : BOOL;
    bActRate : BOOL;
    bWarn : BOOL;
END_VAR
t REF= _Track(eScope, nIndex)^;
r := _Rule(eScope, nIndex);
IF r.nWindow = 0 THEN r.nWindow := 1; END_IF
// fenêtre glissante
IF t.nCount >= r.nWindow THEN
    nOld := INT_TO_USINT((USINT_TO_INT(t.nHead) + 256 - USINT_TO_INT(t.nCount)) MOD 256);
    IF _Bit(t, nOld) AND t.nBadInWindow > 0 THEN t.nBadInWindow := t.nBadInWindow - 1; END_IF
ELSE
    t.nCount := t.nCount + 1;
END_IF
_SetBit(t, t.nHead, bBad);
t.nHead := INT_TO_USINT((USINT_TO_INT(t.nHead) + 1) MOD 256);
IF bBad THEN
    t.nBadInWindow := t.nBadInWindow + 1;
    IF t.nConsec < 255 THEN t.nConsec := t.nConsec + 1; END_IF
    t.nTotalBad := t.nTotalBad + 1;
ELSE
    t.nConsec := 0;
    t.nTotalGood := t.nTotalGood + 1;
END_IF
// évaluation
nConsecAct := SEL(t.nProbationLeft > 0, r.nConsecAction, 2);
IF t.nCount >= r.nMinSample AND t.nCount > 0 THEN fRate := 100.0 * USINT_TO_LREAL(t.nBadInWindow) / USINT_TO_LREAL(t.nCount); ELSE fRate := -1.0; END_IF
bActConsec := nConsecAct > 0 AND t.nConsec >= nConsecAct;
bActRate := r.fRateAction > 0 AND fRate >= r.fRateAction AND t.nProbationLeft = 0;
bWarn := (r.nConsecWarn > 0 AND t.nConsec >= r.nConsecWarn) OR (r.fRateWarn > 0 AND fRate >= r.fRateWarn);
IF t.nProbationLeft > 0 THEN t.nProbationLeft := t.nProbationLeft - 1; END_IF

IF (bActConsec OR bActRate) AND NOT t.bLocked THEN
    t.bLocked := TRUE;
    t.eLevel := E_QualityLevel.Action;
    _React(eScope, nIndex, SEL(bActConsec, r.eReactionRate, r.eReactionConsec));
    _RaiseAlarm(eScope, nIndex, bActConsec, E_Severity.Quality, SEL(bActConsec, LREAL_TO_UDINT(MAX(fRate, 0.0) * 10.0), USINT_TO_UDINT(t.nConsec)));
ELSIF bWarn AND t.eLevel = E_QualityLevel.Normal THEN
    t.eLevel := E_QualityLevel.Warning;
    _RaiseAlarm(eScope, nIndex, t.nConsec >= r.nConsecWarn AND r.nConsecWarn > 0, E_Severity.Warning, USINT_TO_UDINT(t.nConsec));
ELSIF NOT bWarn AND t.eLevel = E_QualityLevel.Warning THEN
    t.eLevel := SEL(t.nProbationLeft > 0, E_QualityLevel.Normal, E_QualityLevel.Probation);
ELSIF t.eLevel = E_QualityLevel.Probation AND t.nProbationLeft = 0 AND NOT t.bLocked THEN
    t.eLevel := E_QualityLevel.Normal;
END_IF
END_METHOD

// Budget de rebuts de l'OF (docs/10 § 4.3) : avertissement à 80 %, arrêt du chargement à 100 %
METHOD RecordOrder
VAR_INPUT
    refOrder : REFERENCE TO ST_OrderState;
    bBad : BOOL;
END_VAR
VAR
    fBudget : LREAL;
    fUsed : LREAL;
END_VAR
IF bBad THEN refOrder.stQuality.nTotalBad := refOrder.stQuality.nTotalBad + 1; ELSE refOrder.stQuality.nTotalGood := refOrder.stQuality.nTotalGood + 1; END_IF
IF _refMachine.stQuality.nOrderBudgetCount > 0 THEN
    fBudget := UDINT_TO_LREAL(_refMachine.stQuality.nOrderBudgetCount);
ELSIF _refMachine.stQuality.fOrderBudgetPct > 0 THEN
    fBudget := UDINT_TO_LREAL(refOrder.nQuantity) * REAL_TO_LREAL(_refMachine.stQuality.fOrderBudgetPct) / 100.0;
ELSE
    RETURN;
END_IF
IF fBudget <= 0 THEN RETURN; END_IF
fUsed := 100.0 * UDINT_TO_LREAL(refOrder.stQuality.nTotalBad) / fBudget;
IF fUsed >= 100.0 AND NOT refOrder.stQuality.bLocked THEN
    refOrder.stQuality.bLocked := TRUE;
    refOrder.stQuality.eLevel := E_QualityLevel.Action;
    bLoadingLocked := TRUE;
    bAnyReaction := TRUE;
    IF __ISVALIDREF(_refAlarms) THEN _refAlarms.Raise(0, 958, LREAL_TO_UDINT(fUsed * 10.0), E_Severity.Quality); END_IF
ELSIF fUsed >= 80.0 AND refOrder.stQuality.eLevel = E_QualityLevel.Normal THEN
    refOrder.stQuality.eLevel := E_QualityLevel.Warning;
    IF __ISVALIDREF(_refAlarms) THEN _refAlarms.Raise(0, 958, LREAL_TO_UDINT(fUsed * 10.0), E_Severity.Warning); END_IF
END_IF
END_METHOD

// Libération (docs/10 § 6) : remise à zéro de la portée, surveillance renforcée pour 10 résultats
METHOD Release : BOOL
VAR_INPUT
    eScope : E_QualityScope;
    nIndex : INT;
END_VAR
VAR
    t : REFERENCE TO ST_QualityTrack;
END_VAR
t REF= _Track(eScope, nIndex)^;
Release := t.bLocked;
IF NOT t.bLocked THEN RETURN; END_IF
_Clear(t);
t.nProbationLeft := 10;
t.eLevel := E_QualityLevel.Probation;
t.bLocked := FALSE;
CASE eScope OF
    E_QualityScope.Station, E_QualityScope.StationImputed:
        IF nIndex >= 1 AND nIndex <= GVL_Const.MAX_STATIONS THEN aStationHold[nIndex] := stTracks.aStation[nIndex].bLocked OR stTracks.aStationImputed[nIndex].bLocked; END_IF
    E_QualityScope.Mover:
        IF nIndex >= 1 AND nIndex <= GVL_Const.MAX_MOVERS THEN aMoverOut[nIndex] := FALSE; END_IF
    E_QualityScope.Group, E_QualityScope.Machine:
        bLoadingLocked := FALSE;
END_CASE
IF __ISVALIDREF(_refAlarms) THEN _refAlarms.Clear(_AlarmSource(eScope, nIndex), _AlarmCode(eScope, nIndex, TRUE)); _refAlarms.Clear(_AlarmSource(eScope, nIndex), _AlarmCode(eScope, nIndex, FALSE)); END_IF
_UpdateAny();
END_METHOD

// Remise à zéro manuelle d'une portée (droit quality.reset)
METHOD Reset
VAR_INPUT
    eScope : E_QualityScope;
    nIndex : INT;
END_VAR
VAR
    t : REFERENCE TO ST_QualityTrack;
END_VAR
t REF= _Track(eScope, nIndex)^;
_Clear(t);
t.eLevel := E_QualityLevel.Normal;
END_METHOD

// Changement de référence produit (D87) : toutes les portées repartent de zéro, sauf les verrous en cours
METHOD ResetAllWindows
VAR
    i : INT;
END_VAR
FOR i := 1 TO GVL_Const.MAX_STATIONS DO _Clear(stTracks.aStation[i]); _Clear(stTracks.aStationImputed[i]); END_FOR
FOR i := 1 TO GVL_Const.MAX_GROUPS DO _Clear(stTracks.aGroup[i]); END_FOR
FOR i := 1 TO GVL_Const.MAX_MOVERS DO _Clear(stTracks.aMover[i]); END_FOR
_Clear(stTracks.stMachine);
END_METHOD

METHOD Publish
GVL_Hmi.stQuality := stTracks;
GVL_Hmi.stQuality.bLoadingLocked := bLoadingLocked;
bMachineStop := FALSE;
END_METHOD

METHOD PRIVATE _Clear
VAR_IN_OUT
    t : ST_QualityTrack;
END_VAR
t.nConsec := 0;
MEMSET(ADR(t.aWindow), 0, SIZEOF(t.aWindow));
t.nHead := 0;
t.nCount := 0;
t.nBadInWindow := 0;
END_METHOD

METHOD PRIVATE _React
VAR_INPUT
    eScope : E_QualityScope;
    nIndex : INT;
    eReaction : E_QualityReaction;
END_VAR
CASE eReaction OF
    E_QualityReaction.StationHold:
        IF (eScope = E_QualityScope.Station OR eScope = E_QualityScope.StationImputed) AND nIndex >= 1 AND nIndex <= GVL_Const.MAX_STATIONS THEN aStationHold[nIndex] := TRUE; END_IF
    E_QualityReaction.MoverOut:
        IF eScope = E_QualityScope.Mover AND nIndex >= 1 AND nIndex <= GVL_Const.MAX_MOVERS THEN aMoverOut[nIndex] := TRUE; END_IF
    E_QualityReaction.StopLoading:
        bLoadingLocked := TRUE;
    E_QualityReaction.MachineStop:
        bMachineStop := TRUE;
END_CASE
_UpdateAny();
END_METHOD

METHOD PRIVATE _UpdateAny
VAR
    i : INT;
END_VAR
bAnyReaction := bLoadingLocked;
FOR i := 1 TO GVL_Const.MAX_STATIONS DO bAnyReaction := bAnyReaction OR aStationHold[i]; END_FOR
FOR i := 1 TO GVL_Const.MAX_MOVERS DO bAnyReaction := bAnyReaction OR aMoverOut[i]; END_FOR
bAnyReaction := bAnyReaction OR stTracks.stMachine.bLocked;
END_METHOD

// Codes 950 à 958 (docs/10 § 8, docs/14 § 3.1)
METHOD PRIVATE _AlarmCode : UINT
VAR_INPUT
    eScope : E_QualityScope;
    nIndex : INT;
    bConsec : BOOL;
END_VAR
CASE eScope OF
    E_QualityScope.Station: _AlarmCode := SEL(bConsec, 951, 950);
    E_QualityScope.StationImputed: _AlarmCode := SEL(bConsec, 953, 952);
    E_QualityScope.Group: _AlarmCode := 954;
    E_QualityScope.Mover: _AlarmCode := 955;
    E_QualityScope.Machine: _AlarmCode := SEL(bConsec, 957, 956);
ELSE
    _AlarmCode := 958;
END_CASE
END_METHOD

METHOD PRIVATE _AlarmSource : UDINT
VAR_INPUT
    eScope : E_QualityScope;
    nIndex : INT;
END_VAR
// station : n° de station × 1000 ; fonction, mover : 0 (machine), le sous-code et le contexte précisent
IF (eScope = E_QualityScope.Station OR eScope = E_QualityScope.StationImputed) AND nIndex >= 1 THEN
    _AlarmSource := INT_TO_UDINT(nIndex) * 1000;
ELSE
    _AlarmSource := 0;
END_IF
END_METHOD

METHOD PRIVATE _RaiseAlarm
VAR_INPUT
    eScope : E_QualityScope;
    nIndex : INT;
    bConsec : BOOL;
    eSeverity : E_Severity;
    nValue : UDINT;
END_VAR
VAR
    stCtx : ST_AlarmContext;
END_VAR
IF NOT __ISVALIDREF(_refAlarms) THEN RETURN; END_IF
IF eScope = E_QualityScope.Mover THEN stCtx.nMover := INT_TO_USINT(nIndex); _refAlarms.SetContext(stCtx); END_IF
_refAlarms.Raise(_AlarmSource(eScope, nIndex), _AlarmCode(eScope, nIndex, bConsec), nValue, eSeverity);
END_METHOD

METHOD PRIVATE _Bit : BOOL
VAR_IN_OUT
    t : ST_QualityTrack;
END_VAR
VAR_INPUT
    n : USINT;
END_VAR
_Bit := (SHR(t.aWindow[n / 8], n MOD 8) AND 1) = 1;
END_METHOD

METHOD PRIVATE _SetBit
VAR_IN_OUT
    t : ST_QualityTrack;
END_VAR
VAR_INPUT
    n : USINT;
    b : BOOL;
END_VAR
IF b THEN
    t.aWindow[n / 8] := t.aWindow[n / 8] OR SHL(BYTE#1, n MOD 8);
ELSE
    t.aWindow[n / 8] := t.aWindow[n / 8] AND NOT SHL(BYTE#1, n MOD 8);
END_IF
END_METHOD
END_FUNCTION_BLOCK
