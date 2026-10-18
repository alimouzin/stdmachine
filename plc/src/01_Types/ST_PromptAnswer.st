// Réponse de l'opérateur (docs/06 § 6.2) ; nStation 0 = calibration machine
{attribute 'pack_mode' := '1'}
TYPE ST_PromptAnswer :
STRUCT
    nSeq : UDINT;
    nStation : USINT;
    nPromptId : UDINT;
    bConfirm : BOOL;                // faux = annuler
    fValue : LREAL;
    sUser : STRING(24);
END_STRUCT
END_TYPE
