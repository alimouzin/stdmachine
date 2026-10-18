// Code d'erreur commune correspondant au refus d'un Start (docs/03 § 1.3)
FUNCTION F_StartRefusalCode : UINT
VAR_INPUT
    eStart : E_StartResult;
END_VAR
CASE eStart OF
    E_StartResult.Busy, E_StartResult.NotReady: F_StartRefusalCode := GVL_Catalog.Err_NotReady;
    E_StartResult.UnknownAction: F_StartRefusalCode := GVL_Catalog.Err_UnknownAction;
    E_StartResult.BadParam: F_StartRefusalCode := GVL_Catalog.Err_BadParam;
    E_StartResult.Interlocked: F_StartRefusalCode := GVL_Catalog.Err_Interlocked;
    E_StartResult.Disabled: F_StartRefusalCode := GVL_Catalog.Err_Disabled;
    E_StartResult.ManualOnly: F_StartRefusalCode := GVL_Catalog.Err_ManualOnly;
    E_StartResult.NotConfigured: F_StartRefusalCode := GVL_Catalog.Err_NotWired;
ELSE
    F_StartRefusalCode := GVL_Catalog.Err_NotReady;
END_CASE
