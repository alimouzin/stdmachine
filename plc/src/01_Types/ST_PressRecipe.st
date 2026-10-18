// Recette de presse (docs/06 § 4)
{attribute 'pack_mode' := '1'}
TYPE ST_PressRecipe :
STRUCT
    bUsed : BOOL;
    nStation : USINT;
    nObject : UINT;
    nRecipeNo : USINT;
    nSegments : USINT;
    aSeg : ARRAY[1..GVL_Const.MAX_PRESS_SEGMENTS] OF ST_PressSegment;
END_STRUCT
END_TYPE
