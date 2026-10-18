// Déclaration d'une variable de station (docs/06 § 2)
{attribute 'pack_mode' := '1'}
TYPE ST_VarInit :
STRUCT
    eType : E_VarType;
    fValue : LREAL;
    stPos : ST_Position;
    bRetain : BOOL;             // persistante (calibration), conservée au redéploiement par identifiant
    nId : UDINT;                // identifiant stable (empreinte de l'Id du projet) ; 0 = variable cachée (D92)
END_STRUCT
END_TYPE
