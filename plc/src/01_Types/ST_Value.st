// Valeur d'un paramètre d'instruction (docs/06 § 2), 11 octets
{attribute 'pack_mode' := '1'}
TYPE ST_Value :
STRUCT
    eSource : E_ValueSource;
    nIndex : INT;               // n° de variable, donnée, paramètre, position, vitesse, texte, objet, variable système
    fValue : LREAL;             // constante ; ObjValue : n° de valeur E1..E4 ; position : décalage ajouté
END_STRUCT
END_TYPE
