// Palpeur Keyence GT2, type 31 (docs/03 § 6.2), mode signaux : S1 AI valeur. Le mode réseau (P1 = 1) n'est pas
// encore écrit : la configuration est refusée.
// Hypothèse : la valeur brute de l'entrée analogique est en micromètres (1 = 0,001 mm), comme la sortie
// analogique du GT2 réglée sur ±32 mm. À confirmer avec le câblage réel.
FUNCTION_BLOCK FB_KeyenceGT2 EXTENDS FB_ObjectBase
VAR
    _fValue : LREAL;                // mm, après remise à zéro
    _fRaw : LREAL;                  // mm, avant remise à zéro
    _fZero : LREAL;
    _bOutOfRange : BOOL;
    _fStabMs : LREAL;
    _nWanted : INT;
    _nTaken : INT;
    _fSum : LREAL;
    _fSimValue : LREAL;
END_VAR
VAR PERSISTENT
    _fZeroKept : LREAL;             // remise à zéro gardée après une coupure
END_VAR

METHOD PROTECTED _OnConfigure : BOOL
IF _bNewObject THEN _fZeroKept := 0; END_IF
_fZero := _fZeroKept;
_fSimValue := _refCfg.aParam[2];
_OnConfigure := _refCfg.aParam[1] = 0 AND F_SignalValid(_refCfg.aSignal[1], E_SignalKind.AI);
END_METHOD

METHOD PROTECTED _OnStart : E_ActStatus
VAR_INPUT
    nAction : UINT;
END_VAR
CASE nAction OF
    GVL_Catalog.KeyenceGT2_Mesurer:
        _nWanted := LIMIT(1, LREAL_TO_INT(_ParamReal(1, _refCfg.aParam[5])), 1000);
        _nTaken := 0;
        _fSum := 0;
        _fStabMs := 0;
        _OnStart := E_ActStatus.Busy;
    GVL_Catalog.KeyenceGT2_RemiseAZero:
        _fZero := _fRaw;
        _fZeroKept := _fZero;
        _OnStart := E_ActStatus.Done;
    GVL_Catalog.KeyenceGT2_LireValeurCourante:
        _SetResult(1, _fValue);
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
    fHigh : LREAL;
    fLow : LREAL;
END_VAR
_OnCycle := E_ActStatus.Busy;
IF _bOutOfRange THEN
    _Fail(GVL_Catalog.KeyenceGT2_Err100, 0);
    _OnCycle := E_ActStatus.Error;
    RETURN;
END_IF
// Stabilisation P6, puis un échantillon par cycle
_fStabMs := _fStabMs + F_CycleMs();
IF _fStabMs < MAX(0.0, _refCfg.aParam[6]) THEN RETURN; END_IF
_fSum := _fSum + _fValue;
_nTaken := _nTaken + 1;
IF _nTaken < _nWanted THEN RETURN; END_IF
fMean := _fSum / INT_TO_LREAL(_nTaken);
fHigh := _refCfg.aParam[2] + ABS(_refCfg.aParam[3]);
fLow := _refCfg.aParam[2] - ABS(_refCfg.aParam[4]);
_SetResult(1, fMean);
IF fMean > fHigh THEN
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
_bOutOfRange := nRaw = 32767 OR nRaw = -32768;
_fRaw := INT_TO_LREAL(nRaw) * 0.001;
_fValue := _fRaw - _fZero;
END_METHOD

// Simulation : valeur de référence P2 avec un bruit de ±10 % de la tolérance haute (docs/03 § 6.2)
METHOD PROTECTED _Simulate
_fRaw := _fSimValue + (F_SimRandom() - 0.5) * 0.2 * ABS(_refCfg.aParam[3]) + _fZero;
IF _Forced(1) THEN _fRaw := _ForcedValue(1) + _fZero; END_IF       // valeur forcée en mm
_bOutOfRange := FALSE;
_fValue := _fRaw - _fZero;
END_METHOD

METHOD PROTECTED _Publish
_stStatus.aValue[1] := _fValue;
END_METHOD
END_FUNCTION_BLOCK
