// Données déployées d'une station (docs/06 § 4)
{attribute 'pack_mode' := '1'}
TYPE ST_StationData :
STRUCT
    nObjCount : UINT;
    aObj : ARRAY[1..GVL_Const.MAX_OBJ_PER_STATION] OF ST_ObjConfig;   // parents avant enfants
    aTable : ARRAY[1..GVL_Const.MAX_TABLES] OF ST_TableDir;
    nInstrCount : INT;
    aInstr : ARRAY[1..GVL_Const.MAX_INSTR] OF ST_Instr;
    aCond : ARRAY[1..GVL_Const.MAX_COND] OF ST_Condition;
    aText : ARRAY[1..GVL_Const.MAX_TEXTS] OF STRING(80);
    aPos : ARRAY[1..GVL_Const.MAX_NAMED_POS] OF ST_NamedPos;
    aVar : ARRAY[1..GVL_Const.MAX_VARS] OF ST_VarInit;
    aInterlock : ARRAY[1..GVL_Const.MAX_INTERLOCKS] OF ST_Interlock;
    nOpMapCount : USINT;
    aOpMap : ARRAY[1..GVL_Const.MAX_OPMAP] OF ST_OpMap;
    nCalibCount : USINT;
    aCalib : ARRAY[1..GVL_Const.CALIB_PER_STATION] OF ST_CalibDef;
END_STRUCT
END_TYPE
