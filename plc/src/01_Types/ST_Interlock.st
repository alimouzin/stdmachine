// Verrouillage (docs/06 § 2)
{attribute 'pack_mode' := '1'}
TYPE ST_Interlock :
STRUCT
    nObject : UINT;             // objet protégé
    nActionMask : UDINT;        // bit n-1 = action n ; 0 = toutes les actions de mouvement
    nCond : INT;                // condition qui doit rester vraie
END_STRUCT
END_TYPE
