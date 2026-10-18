// Vitesse d'un paramètre résolu, en % de la Vmax (docs/04 § 12) : n° de vitesse V1..V8 ou % direct
FUNCTION F_ParamSpeed : LREAL
VAR_INPUT
    refParams : REFERENCE TO ST_ActParams;
    nParam : USINT;
    refCfg : REFERENCE TO ST_ObjConfig;
    nDefaultSpeed : USINT;       // V utilisé si le paramètre est absent
END_VAR
VAR
    nV : INT;
END_VAR
IF nParam >= 1 AND nParam <= GVL_Const.MAX_PARAMS THEN
    CASE refParams.aKind[nParam] OF
        E_ParamKind.SpeedPercent:
            F_ParamSpeed := LIMIT(0.0, refParams.aReal[nParam], 100.0);
            RETURN;
        E_ParamKind.SpeedIndex, E_ParamKind.Real:
            nV := LREAL_TO_INT(refParams.aReal[nParam]);
            IF nV >= 1 AND nV <= GVL_Const.MAX_SPEEDS AND refCfg.aSpeedPct[nV] > 0 THEN
                F_ParamSpeed := refCfg.aSpeedPct[nV];
                RETURN;
            END_IF
    END_CASE
END_IF
IF nDefaultSpeed >= 1 AND nDefaultSpeed <= GVL_Const.MAX_SPEEDS AND refCfg.aSpeedPct[nDefaultSpeed] > 0 THEN
    F_ParamSpeed := refCfg.aSpeedPct[nDefaultSpeed];
ELSE
    F_ParamSpeed := 50.0;
END_IF
