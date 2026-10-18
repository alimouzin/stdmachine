// Motif d'un refus de commande, traduit par la HMI (docs/06 § 6.1)
{attribute 'qualified_only'}
{attribute 'strict'}
TYPE E_RejectReason :
(
    None := 0,
    WrongState := 1,          // la commande n'est pas acceptée dans l'état courant
    StartBlocked := 2,        // état déclaré bloquant (docs/12 § 2)
    WrongMode := 3,           // mode Production / Maintenance
    NotAllowed := 4,          // station non réintégrable, cible inconnue
    Busy := 5,                // une opération ou une commande est en cours
    NotConfigured := 6,       // élément absent du déploiement
    TableFull := 7,           // table d'OF pleine
    PartsInProcess := 8,      // annulation d'un OF qui a encore des pièces
    Incompatible := 9         // projet plus récent que la bibliothèque (docs/02 § 7.3)
) USINT;
END_TYPE
