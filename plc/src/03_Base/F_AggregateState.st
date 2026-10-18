// État agrégé d'un parent : En erreur > Non initialisé > En cours > Prêt (docs/04 § 8)
FUNCTION F_AggregateState : E_ObjState
VAR_INPUT
    eCurrent : E_ObjState;       // agrégat en cours de calcul
    eChild : E_ObjState;
END_VAR
IF eCurrent = E_ObjState.Error OR eChild = E_ObjState.Error THEN
    F_AggregateState := E_ObjState.Error;
ELSIF eCurrent = E_ObjState.NotInitialized OR eChild = E_ObjState.NotInitialized THEN
    F_AggregateState := E_ObjState.NotInitialized;
ELSIF eCurrent = E_ObjState.Busy OR eChild = E_ObjState.Busy THEN
    F_AggregateState := E_ObjState.Busy;
ELSE
    F_AggregateState := E_ObjState.Ready;
END_IF
