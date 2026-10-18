// Balise pilotée par le séquenceur auquel elle est rattachée (docs/02 § 8, docs/03 § 8.1)
INTERFACE I_Beacon
METHOD SetPattern
VAR_INPUT
    nPattern : UINT;             // bits de F_BeaconBits
    bBuzzer : BOOL;
END_VAR
END_METHOD
END_INTERFACE
