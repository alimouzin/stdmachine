// Statut d'un objet publié à chaque cycle (docs/04 § 5)
{attribute 'pack_mode' := '1'}
TYPE ST_ObjStatus :
STRUCT
    eState : E_ObjState;
    eActStatus : E_ActStatus;
    nAction : USINT;            // action en cours ou dernière action
    bManual : BOOL;
    nErrorCode : UINT;
    nSubCode : UDINT;
    fEffOverride : REAL;        // %
    bOverrideHalted : BOOL;     // mouvement suspendu par override à 0 %
    bSimulated : BOOL;
    nInterlock : USINT;         // verrouillage qui bloque, 0 = aucun
    fProgress : REAL;           // 0..1 avancement estimé, -1 = inconnu
    nElapsedMs : UDINT;
    nActionCount : UDINT;       // maintenance, persistant
    nErrorCount : UDINT;        // maintenance, persistant
    fMotionHours : REAL;        // maintenance, persistant
    nTimeoutMs : UDINT;         // timeout effectif estimé (affichage)
    aValue : ARRAY[1..GVL_Const.MAX_VALUES] OF LREAL;   // E1..E4
END_STRUCT
END_TYPE
