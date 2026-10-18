// Pointeurs vers l'image E/S et les pools matériels du projet de la machine (docs/04 § 6).
// GVL_IO et GVL_Hw sont dans le projet de la machine (reliés aux bornes) ; MAIN les donne ici au premier cycle :
//     GVL_IoRef.pDI := ADR(GVL_IO.aDI); … ; GVL_IoRef.pAxis := ADR(GVL_Hw.aAxis); GVL_IoRef.nAxes := …
{attribute 'qualified_only'}
VAR_GLOBAL
    pDI : POINTER TO ARRAY[0..GVL_Const.MAX_DI - 1] OF BOOL;
    pDO : POINTER TO ARRAY[0..GVL_Const.MAX_DO - 1] OF BOOL;
    pAI : POINTER TO ARRAY[0..GVL_Const.MAX_AI - 1] OF INT;
    pAO : POINTER TO ARRAY[0..GVL_Const.MAX_AO - 1] OF INT;
    pAxis : POINTER TO ARRAY[1..GVL_Const.MAX_AXES] OF AXIS_REF;   // GVL_Hw.aAxis
    nAxes : INT;                     // nombre d'axes reliés dans GVL_Hw
    // Rempli par FB_Machine au premier cycle, pour les objets Système planaire et Mover
    ipPlanar : I_PlanarDriver;
    ipMovers : I_MoverMotion;
END_VAR
