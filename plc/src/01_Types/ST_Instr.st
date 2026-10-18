// Instruction (docs/06 § 2), 154 octets
{attribute 'pack_mode' := '1'}
TYPE ST_Instr :
STRUCT
    eOp : E_Op;
    nStepId : UDINT;            // carte de l'éditeur ; 0 = instruction technique
    nObject : UINT;             // Action : n° local de l'objet
    nAction : UINT;             // Action : n° d'action du catalogue
    aParam : ARRAY[1..GVL_Const.MAX_PARAMS] OF ST_Value;
    aBind : ARRAY[1..GVL_Const.MAX_PARAMS] OF ST_Bind;
    aArg : ARRAY[1..GVL_Const.MAX_ARGS] OF DINT;
    nTimeoutMs : UDINT;         // 0 = défaut (configuration de l'objet, puis catalogue)
    eOnFail : E_FailPolicy;
    nRetries : USINT;
    nFailTarget : INT;          // Goto : index d'instruction
END_STRUCT
END_TYPE
