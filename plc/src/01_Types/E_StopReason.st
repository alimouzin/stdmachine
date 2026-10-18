// Raison du dernier arrêt de la machine (docs/05 § 5.2, docs/14 § 4) : la cause système quand aucune alarme
// ne l'explique. Publiée dans ST_MachineStatus pour la HMI (toasts « OF terminé », « Machine vidée ») (D94)
{attribute 'qualified_only'}
{attribute 'strict'}
TYPE E_StopReason :
(
    None := 0,
    Alarm := 1,             // première alarme cause : nStopCauseGlobalId, nStopCauseCode
    Operator := 2,
    OrderComplete := 3,
    Drained := 4,
    Quality := 5
) USINT;
END_TYPE
