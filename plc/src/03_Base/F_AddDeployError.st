// Ajoute une erreur à la réponse de déploiement (docs/05 § 10, étape 5) ; les codes sont listés dans FB_Loader
FUNCTION F_AddDeployError : BOOL
VAR_IN_OUT
    stResp : ST_DeployResponse;
END_VAR
VAR_INPUT
    nCode : UINT;
    nStation : USINT;
    nTable : USINT;
    nInstr : INT;
    nObject : UINT;
    nDetail : UDINT;
END_VAR
F_AddDeployError := FALSE;
IF stResp.nErrors >= 32 THEN RETURN; END_IF       // les suivantes sont perdues : 32 suffisent pour corriger
stResp.nErrors := stResp.nErrors + 1;
stResp.aError[stResp.nErrors].nCode := nCode;
stResp.aError[stResp.nErrors].nStation := nStation;
stResp.aError[stResp.nErrors].nTable := nTable;
stResp.aError[stResp.nErrors].nInstr := nInstr;
stResp.aError[stResp.nErrors].nObject := nObject;
stResp.aError[stResp.nErrors].nDetail := nDetail;
F_AddDeployError := TRUE;
END_FUNCTION
