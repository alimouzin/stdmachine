// Configuration déployée de la surveillance des rebuts (docs/10 § 9)
{attribute 'pack_mode' := '1'}
TYPE ST_QualityCfg :
STRUCT
    aDefault : ARRAY[0..5] OF ST_QualityRule;                   // par E_QualityScope
    aStationRule : ARRAY[1..GVL_Const.MAX_STATIONS] OF ST_QualityRule;
    aStationInherits : ARRAY[1..GVL_Const.MAX_STATIONS] OF BOOL;
    aGroupRule : ARRAY[1..GVL_Const.MAX_GROUPS] OF ST_QualityRule;
    aGroupInherits : ARRAY[1..GVL_Const.MAX_GROUPS] OF BOOL;
    aGroupImpute : ARRAY[1..GVL_Const.MAX_GROUPS] OF UINT;      // masque des fonctions amont imputées, 0 = n'impute pas
    nOrderBudgetCount : UDINT;                                  // budget de rebuts d'OF en nombre, 0 = aucun
    fOrderBudgetPct : REAL;                                     // ou en %, 0 = aucun
END_STRUCT
END_TYPE
