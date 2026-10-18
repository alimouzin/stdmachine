// Pesage, type 33 (docs/03 § 6.4), mode analogique : S1 AI mesure. Le mode réseau n'est pas encore écrit.
// Hypothèses : la valeur brute est en dixièmes de l'unité P1 (g ou mg) ; la mesure est stable quand l'écart
// entre la plus grande et la plus petite des 10 dernières valeurs ne dépasse pas 0,5 unité ; tolérance basse
// P3 négative ou positive, prise en valeur absolue sous la valeur nominale P4.
FUNCTION_BLOCK FB_Scale EXTENDS FB_ObjectBase
VAR
    _fGross : LREAL;
    _fValue : LREAL;
    _bOverload : BOOL;
    _aWin : ARRAY[1..10] OF LREAL;
    _nWin : INT;
    _nNext : INT := 1;
    _bStable : BOOL;
    _fStabMs : LREAL;
    _nWanted : INT;
    _nTaken : INT;
    _fSum : LREAL;
END_VAR
VAR PERSISTENT
    _fTare : LREAL;
END_VAR

METHOD PROTECTED _OnConfigure : BOOL
IF _bNewObject THEN _fTare := 0; END_IF
_nWin := 0;
_nNext := 1;
_OnConfigure := F_SignalValid(_refCfg.aSignal[1], E_SignalKind.AI);
END_METHOD

METHOD PROTECTED _OnStart : E_ActStatus
VAR_INPUT
    nAction : UINT;
END_VAR
CASE nAction OF
    GVL_Catalog.Scale_Tarer, GVL_Catalog.Scale_Mesurer:
        _nWanted := LIMIT(1, LREAL_TO_INT(_ParamReal(1, _refCfg.aParam[5])), 1000);
        _nTaken := 0;
        _fSum := 0;
        _fStabMs := 0;
        _OnStart := E_ActStatus.Busy;
    GVL_Catalog.Scale_LireValeurCourante:
        _SetResult(1, _fValue);
        _SetResult(2, SEL(_bStable, 0.0, 1.0));
        _OnStart := E_ActStatus.Done;
ELSE
    _OnStart := E_ActStatus.Error;
END_CASE
END_METHOD

METHOD PROTECTED _OnCycle : E_ActStatus
VAR_INPUT
    nAction : UINT;
END_VAR
VAR
    fMean : LREAL;
    fLow : LREAL;
END_VAR
_OnCycle := E_ActStatus.Busy;
IF _bOverload THEN
    _Fail(GVL_Catalog.Scale_Err101, 0);
    _OnCycle := E_ActStatus.Error;
    RETURN;
END_IF
_fStabMs := _fStabMs + F_CycleMs();
IF _fStabMs < MAX(0.0, _refCfg.aParam[6]) THEN RETURN; END_IF
IF NOT _bStable THEN
    // Pas stable deux fois le temps de stabilisation après son début : erreur 100
    IF _fStabMs >= 2.0 * MAX(100.0, _refCfg.aParam[6]) THEN
        _Fail(GVL_Catalog.Scale_Err100, 0);
        _OnCycle := E_ActStatus.Error;
    END_IF
    RETURN;
END_IF
IF nAction = GVL_Catalog.Scale_Tarer THEN
    _fTare := _fGross;
    _OnCycle := E_ActStatus.Done;
    RETURN;
END_IF
_fSum := _fSum + _fValue;
_nTaken := _nTaken + 1;
IF _nTaken < _nWanted THEN RETURN; END_IF
fMean := _fSum / INT_TO_LREAL(_nTaken);
_SetResult(1, fMean);
fLow := _refCfg.aParam[4] - ABS(_refCfg.aParam[3]);
IF _refCfg.aParam[2] = 0 AND _refCfg.aParam[3] = 0 THEN
    _SetJudgement(2, E_Judgement.Ok);           // pas de tolérance : pas de jugement
ELSIF fMean > _refCfg.aParam[4] + ABS(_refCfg.aParam[2]) THEN
    _SetJudgement(2, E_Judgement.High);
ELSIF fMean < fLow THEN
    _SetJudgement(2, E_Judgement.Low);
ELSE
    _SetJudgement(2, E_Judgement.Ok);
END_IF
_OnCycle := E_ActStatus.Done;
END_METHOD

METHOD PROTECTED _ReadHw
VAR
    nRaw : INT;
END_VAR
nRaw := F_ReadAI(_refCfg.aSignal[1]);
_bOverload := nRaw = 32767;
_Sample(INT_TO_LREAL(nRaw) * 0.1);
END_METHOD

// Simulation : valeur nominale P4 (plus la tare) avec un bruit de ±0,1 unité
METHOD PROTECTED _Simulate
VAR
    fGross : LREAL;
END_VAR
fGross := _refCfg.aParam[4] + _fTare + (F_SimRandom() - 0.5) * 0.2;
IF _Forced(1) THEN fGross := _ForcedValue(1) + _fTare; END_IF       // valeur forcée nette, en unité
_bOverload := FALSE;
_Sample(fGross);
END_METHOD

METHOD PRIVATE _Sample
VAR_INPUT
    fGross : LREAL;
END_VAR
VAR
    i : INT;
    fMin : LREAL := 1.0E38;
    fMax : LREAL := -1.0E38;
END_VAR
_fGross := fGross;
_fValue := fGross - _fTare;
_aWin[_nNext] := fGross;
_nNext := _nNext MOD 10 + 1;
_nWin := MIN(_nWin + 1, 10);
FOR i := 1 TO _nWin DO
    fMin := MIN(fMin, _aWin[i]);
    fMax := MAX(fMax, _aWin[i]);
END_FOR
_bStable := _nWin = 10 AND fMax - fMin <= 0.5;
END_METHOD

METHOD PROTECTED _Publish
_stStatus.aValue[1] := _fValue;
_stStatus.aValue[2] := SEL(_bStable, 0.0, 1.0);
END_METHOD
END_FUNCTION_BLOCK
