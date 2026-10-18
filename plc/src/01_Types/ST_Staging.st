// Zone d'attente du déploiement (docs/06 § 6.2)
{attribute 'pack_mode' := '1'}
TYPE ST_Staging :
STRUCT
    stMachine : ST_MachineData;
    aStation : ARRAY[1..GVL_Const.MAX_STATIONS] OF ST_StationData;
    aPressRecipe : ARRAY[1..GVL_Const.MAX_PRESS_RECIPES] OF ST_PressRecipe;
END_STRUCT
END_TYPE
