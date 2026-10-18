// Résultats d'une action (docs/04 § 5)
{attribute 'pack_mode' := '1'}
TYPE ST_ActResults :
STRUCT
    nCount : USINT;
    aReal : ARRAY[1..GVL_Const.MAX_PARAMS] OF LREAL;
    aJudgement : ARRAY[1..GVL_Const.MAX_PARAMS] OF E_Judgement;
    stPos : ST_Position;        // le résultat de type Position, s'il y en a un
    nPosIndex : USINT;          // rang du résultat de type Position, 0 = aucun
    sText : STRING(80);         // le résultat de type Text, s'il y en a un
    nTextIndex : USINT;         // rang du résultat de type Text, 0 = aucun
END_STRUCT
END_TYPE
