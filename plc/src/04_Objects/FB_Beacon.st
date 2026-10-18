// Balise, type 50 (docs/03 § 8.1) : pilotée par le séquenceur ; actions de test en manuel seulement
FUNCTION_BLOCK FB_Beacon EXTENDS FB_ObjectBase IMPLEMENTS I_Beacon
VAR
    _nPattern : UINT;
    _bBuzzerReq : BOOL;
    _fBlink : LREAL;
    _bPhase : BOOL;
    _nTestColor : INT := -1;        // test manuel : 0 rouge, 1 orange, 2 vert, 3 bleu, 4 buzzer
    _fTestMs : LREAL;
    _fBuzzMs : LREAL;               // durée de buzzer demandée (0 = jusqu'à acquittement)
    _fBuzzElapsed : LREAL;
    _aOut : ARRAY[1..5] OF BOOL;    // rouge, orange, vert, bleu, buzzer
END_VAR

METHOD SetPattern
VAR_INPUT
    nPattern : UINT;
    bBuzzer : BOOL;
END_VAR
IF bBuzzer AND NOT _bBuzzerReq THEN _fBuzzElapsed := 0; END_IF
_nPattern := nPattern;
_bBuzzerReq := bBuzzer;
END_METHOD

METHOD PROTECTED _OnStart : E_ActStatus
VAR_INPUT
    nAction : UINT;
END_VAR
_fBuzzElapsed := 0;
CASE nAction OF
    GVL_Catalog.Beacon_Tester:
        _nTestColor := LIMIT(0, LREAL_TO_INT(_ParamReal(1, 0.0)), 4);
        _fTestMs := MAX(_ParamReal(2, 2000.0), 100.0);
        _OnStart := E_ActStatus.Busy;
    GVL_Catalog.Beacon_Buzzer:
        _nTestColor := 4;
        _fTestMs := MAX(_ParamReal(1, 1000.0), 100.0);
        _OnStart := E_ActStatus.Busy;
ELSE
    _OnStart := E_ActStatus.Error;
END_CASE
END_METHOD

METHOD PROTECTED _OnCycle : E_ActStatus
VAR_INPUT
    nAction : UINT;
END_VAR
IF _fElapsedMs >= _fTestMs THEN
    _nTestColor := -1;
    _OnCycle := E_ActStatus.Done;
ELSE
    _OnCycle := E_ActStatus.Busy;
END_IF
END_METHOD

METHOD PROTECTED _OnAbort
VAR_INPUT
    nAction : UINT;
END_VAR
_nTestColor := -1;
END_METHOD

METHOD PROTECTED _Monitor
VAR
    fPeriod : LREAL;
    i : INT;
END_VAR
fPeriod := MAX(_refCfg.aParam[1], 100.0);
_fBlink := _fBlink + F_CycleMs();
IF _fBlink >= fPeriod / 2.0 THEN
    _fBlink := 0;
    _bPhase := NOT _bPhase;
END_IF
FOR i := 1 TO 5 DO _aOut[i] := FALSE; END_FOR
IF _nTestColor >= 0 THEN
    _aOut[_nTestColor + 1] := TRUE;
    RETURN;
END_IF
_aOut[1] := (_nPattern AND GVL_BeaconBits.RED) <> 0 OR ((_nPattern AND GVL_BeaconBits.RED_BLINK) <> 0 AND _bPhase);
_aOut[2] := (_nPattern AND GVL_BeaconBits.ORANGE) <> 0 OR ((_nPattern AND GVL_BeaconBits.ORANGE_BLINK) <> 0 AND _bPhase);
_aOut[3] := (_nPattern AND GVL_BeaconBits.GREEN) <> 0 OR ((_nPattern AND GVL_BeaconBits.GREEN_BLINK) <> 0 AND _bPhase);
_aOut[4] := (_nPattern AND GVL_BeaconBits.BLUE) <> 0 OR ((_nPattern AND GVL_BeaconBits.BLUE_BLINK) <> 0 AND _bPhase);
IF _bBuzzerReq THEN
    _fBuzzElapsed := _fBuzzElapsed + F_CycleMs();
    _aOut[5] := _refCfg.aParam[2] <= 0 OR _fBuzzElapsed < _refCfg.aParam[2];
END_IF
END_METHOD

METHOD PROTECTED _WriteHw
VAR
    i : USINT;
END_VAR
FOR i := 1 TO 5 DO F_WriteDO(_refCfg.aSignal[i], _aOut[i]); END_FOR
END_METHOD

METHOD PROTECTED _Publish
VAR
    i : INT;
    nBits : DWORD;
END_VAR
FOR i := 1 TO 5 DO
    IF _aOut[i] THEN nBits := nBits OR SHL(DWORD#1, i - 1); END_IF
END_FOR
_stStatus.aValue[1] := DWORD_TO_LREAL(nBits);
END_METHOD
END_FUNCTION_BLOCK
