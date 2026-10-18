// Commande d'état (docs/06 § 6.2) ; nTarget 0 = machine, 1..16 = station
{attribute 'pack_mode' := '1'}
TYPE ST_CmdRequest :
STRUCT
    nSeq : UDINT;
    nTarget : USINT;
    eCmd : E_Cmd;
    bSetMode : BOOL;                // changement de mode au lieu d'une commande
    eMode : E_Mode;
    sUser : STRING(24);
END_STRUCT
END_TYPE
