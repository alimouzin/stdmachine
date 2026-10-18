// Demande à l'opérateur en cours (docs/05 § 10.1)
{attribute 'pack_mode' := '1'}
TYPE ST_PromptState :
STRUCT
    nPromptId : UDINT;              // 0 = aucune demande
    nText : INT;                    // n° de texte de la table
    bInput : BOOL;                  // saisie d'une valeur (sinon confirmation)
END_STRUCT
END_TYPE
