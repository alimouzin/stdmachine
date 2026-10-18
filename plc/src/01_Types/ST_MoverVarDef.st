// Déclaration d'une variable de mover (docs/05 § 5.8)
{attribute 'pack_mode' := '1'}
TYPE ST_MoverVarDef :
STRUCT
    nId : UDINT;                // identifiant stable
    sName : STRING(24);
    eType : E_VarType;
    sUnit : STRING(8);
    fInit : LREAL;
    stInitPos : ST_Position;
    eReset : E_MoverVarReset;
END_STRUCT
END_TYPE
