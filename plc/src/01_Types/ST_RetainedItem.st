// Valeur rémanente sauvegardée par la HMI, rangée par identifiant (docs/02 § 7.4)
{attribute 'pack_mode' := '1'}
TYPE ST_RetainedItem :
STRUCT
    eKind : E_RetainedKind;
    nStation : USINT;               // 0 = machine
    nMover : USINT;
    nId : UDINT;                    // variable, objet ou calibration
    aValue : ARRAY[1..3] OF LREAL;  // valeur ; compteurs : actions, erreurs, heures ; calibration : état
    stPos : ST_Position;
    tStamp : LINT;                  // date de la valeur (calibration)
    sUser : STRING(24);
END_STRUCT
END_TYPE
