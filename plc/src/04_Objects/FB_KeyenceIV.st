// Vision Keyence IV, type 30 (docs/03 § 6.1), mode signaux : S1 DO déclenchement, S2 DI prêt, S3 DI résultat OK,
// S4 DI résultat NOK. Le mode réseau (P1 = 1) n'est pas encore écrit : la configuration est refusée.
// Hypothèse : en mode signaux, le programme ne se change pas par les signaux ; « Sélectionner programme »
// mémorise le n° demandé (affiché, rendu par la HMI) sans le transmettre à l'appareil.
FUNCTION_BLOCK FB_KeyenceIV EXTENDS FB_ObjectBase
VAR
    _bTrigger : BOOL;
    _bReady : BOOL;
    _bOk : BOOL;
    _bNok : BOOL;
    _fWaitMs : LREAL;
    _nProgram : INT;
    _eLast : E_Judgement;
    _fLastValue : LREAL;
    _bSimResult : BOOL;             // simulation : un résultat est présent
    _bSimOk : BOOL;
END_VAR

METHOD PROTECTED _OnConfigure : BOOL
_nProgram := LREAL_TO_INT(_refCfg.aParam[2]);
_OnConfigure := _refCfg.aParam[1] = 0
                AND F_SignalValid(_refCfg.aSignal[1], E_SignalKind.DO)
                AND F_SignalValid(_refCfg.aSignal[3], E_SignalKind.DI)
                AND F_SignalValid(_refCfg.aSignal[4], E_SignalKind.DI);
END_METHOD

METHOD PROTECTED _OnStart : E_ActStatus
VAR_INPUT
    nAction : UINT;
END_VAR
CASE nAction OF
    GVL_Catalog.KeyenceIV_SelectionnerProgramme:
        _nProgram := LREAL_TO_INT(_ParamReal(1, _refCfg.aParam[2]));
        _OnStart := E_ActStatus.Done;
    GVL_Catalog.KeyenceIV_Declencher:
        IF F_SignalValid(_refCfg.aSignal[2], E_SignalKind.DI) AND NOT _bReady THEN
            _Fail(GVL_Catalog.KeyenceIV_Err100, 0);
            _OnStart := E_ActStatus.Error;
            RETURN;
        END_IF
        _fWaitMs := 0;
        _bTrigger := TRUE;
        _bSimResult := FALSE;
        _OnStart := E_ActStatus.Busy;
    GVL_Catalog.KeyenceIV_LireDernierResultat:
        _SetJudgement(1, _eLast);
        _SetResult(2, _fLastValue);
        _OnStart := E_ActStatus.Done;
ELSE
    _OnStart := E_ActStatus.Error;
END_CASE
END_METHOD

METHOD PROTECTED _OnCycle : E_ActStatus
VAR_INPUT
    nAction : UINT;
END_VAR
_OnCycle := E_ActStatus.Busy;
_fWaitMs := _fWaitMs + F_CycleMs();
// Le résultat est lu après l'attente P3, quand l'une des deux sorties du capteur est active
IF _fWaitMs < MAX(0.0, _refCfg.aParam[3]) OR NOT (_bOk XOR _bNok) THEN RETURN; END_IF
_bTrigger := FALSE;
IF _bOk THEN _eLast := E_Judgement.Ok; ELSE _eLast := E_Judgement.Nok; END_IF
_fLastValue := 0;                   // pas de valeur mesurée en mode signaux
_SetJudgement(1, _eLast);
_SetResult(2, _fLastValue);
_OnCycle := E_ActStatus.Done;
END_METHOD

METHOD PROTECTED _OnAbort
VAR_INPUT
    nAction : UINT;
END_VAR
_bTrigger := FALSE;
END_METHOD

METHOD PROTECTED _SafeState
_bTrigger := FALSE;
END_METHOD

METHOD PROTECTED _ReadHw
_bReady := F_ReadDI(_refCfg.aSignal[2]);
_bOk := F_ReadDI(_refCfg.aSignal[3]);
_bNok := F_ReadDI(_refCfg.aSignal[4]);
END_METHOD

METHOD PROTECTED _WriteHw
F_WriteDO(_refCfg.aSignal[1], _bTrigger);
END_METHOD

// Simulation : prêt ; à chaque déclenchement, jugement bon avec une probabilité de 95 % (docs/03 § 6.1)
METHOD PROTECTED _Simulate
IF _bTrigger AND NOT _bSimResult THEN
    _bSimResult := TRUE;
    _bSimOk := F_SimRandom() < 0.95;
END_IF
_bReady := TRUE;
_bOk := _bSimResult AND _bSimOk;
_bNok := _bSimResult AND NOT _bSimOk;
IF _Forced(2) THEN _bReady := _ForcedValue(2) >= 0.5; END_IF
IF _Forced(3) THEN _bOk := _ForcedValue(3) >= 0.5; END_IF
IF _Forced(4) THEN _bNok := _ForcedValue(4) >= 0.5; END_IF
END_METHOD

METHOD PROTECTED _Publish
_stStatus.aValue[1] := TO_LREAL(_eLast);
_stStatus.aValue[2] := INT_TO_LREAL(_nProgram);
END_METHOD
END_FUNCTION_BLOCK
