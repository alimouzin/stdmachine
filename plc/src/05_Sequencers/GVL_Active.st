// Zone active du déploiement (docs/05 § 9 et § 10) : persistante, le PLC redémarre Stopped avec le dernier
// déploiement. À vérifier sur la cible : la taille (environ 1,5 Mo) des données persistantes.
{attribute 'qualified_only'}
VAR_GLOBAL PERSISTENT
    stMachine : ST_MachineData;
    aStation : ARRAY[1..GVL_Const.MAX_STATIONS] OF ST_StationData;
    aPressRecipe : ARRAY[1..GVL_Const.MAX_PRESS_RECIPES] OF ST_PressRecipe;
    bDeployed : BOOL;               // un déploiement a été activé
    nRetainId : ULINT;              // identifiant de la mémoire rémanente (docs/02 § 7.4), tiré au premier démarrage
END_VAR
