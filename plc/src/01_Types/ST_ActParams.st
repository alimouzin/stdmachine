// Paramètres résolus d'une action (docs/04 § 5)
{attribute 'pack_mode' := '1'}
TYPE ST_ActParams :
STRUCT
    aKind : ARRAY[1..GVL_Const.MAX_PARAMS] OF E_ParamKind;
    aReal : ARRAY[1..GVL_Const.MAX_PARAMS] OF LREAL;   // valeur, ou n° de vitesse, ou % de vitesse
    stPos : ST_Position;                               // le paramètre de type Position (un au plus)
    sText : STRING(80);                                // le paramètre de type Text (un au plus)
    nTimeoutMs : UDINT;                                // 0 = celui de la configuration ou du catalogue
END_STRUCT
END_TYPE
