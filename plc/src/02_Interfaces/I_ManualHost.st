// Ce que le canal manuel lit de son hôte : une station, ou la machine pour le canal 0 (docs/05 § 7)
INTERFACE I_ManualHost
// Objet de n° local nLocalId, 0 si absent
METHOD ObjectAt : I_Object
VAR_INPUT
    nLocalId : UINT;
END_VAR
END_METHOD
// Position nommée à apprendre, 0 si l'hôte n'en a pas (la machine n'a pas de positions nommées)
METHOD NamedPos : POINTER TO ST_NamedPos
VAR_INPUT
    nIndex : INT;
END_VAR
END_METHOD
PROPERTY State : E_State
GET
END_GET
END_PROPERTY
PROPERTY Mode : E_Mode
GET
END_GET
END_PROPERTY
END_INTERFACE
