// Verrouillages d'une station (docs/04 § 4) : 0 si autorisé, sinon n° du verrouillage violé
INTERFACE I_InterlockProvider
METHOD Check : INT
VAR_INPUT
    nLocalId : UINT;
    nAction : UINT;
END_VAR
END_METHOD
END_INTERFACE
