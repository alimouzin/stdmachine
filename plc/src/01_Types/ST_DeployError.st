// Erreur rendue par le chargeur (docs/05 § 10)
{attribute 'pack_mode' := '1'}
TYPE ST_DeployError :
STRUCT
    nCode : UINT;                   // voir FB_Loader
    nStation : USINT;               // 0 = machine
    nTable : USINT;
    nInstr : INT;
    nObject : UINT;
    nDetail : UDINT;
END_STRUCT
END_TYPE
