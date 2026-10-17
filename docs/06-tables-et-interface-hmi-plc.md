# Format des tables et interface HMI–PLC

Ce document définit ce que la HMI envoie au PLC (configuration et tables compilées), les règles de compilation des process, les symboles ADS échangés, les handshakes et l'interface C# `IPlcClient` que la HMI utilise, qu'elle parle au vrai PLC ou au simulateur. Les algorithmes qui exécutent ces données sont dans `05-plc-sequenceurs.md`.

## 1. Ce qui est déployé

| Bloc | Contenu | Taille max |
| --- | --- | --- |
| Machine | Identité, système planaire, fonctions, stations (fonctions, positions planaires), positions planaires, objets de niveau machine, process général compilé, paramètres machine | ≈ 200 Ko |
| Station (× 16) | Objets (configurations), répertoire des tables, instructions, conditions, textes, positions nommées, variables (valeurs initiales), verrouillages | ≈ 130 Ko chacun |
| Recettes de presse | Recettes de toutes les presses | ≈ 30 Ko |
| Image E/S (lecture seule, sens PLC → HMI) | Rien n'est déployé : la HMI importe la liste des index depuis le CSV exporté de TwinCAT | — |

Chaque bloc est écrit dans la zone d'attente puis vérifié avec sa somme de contrôle CRC-32 (polynôme IEEE 802.3, calculée sur les octets de la structure telle qu'écrite).

## 2. Structures communes

Toutes en `{attribute 'pack_mode' := '1'}`. Côté C# : `[StructLayout(LayoutKind.Sequential, Pack = 1)]`, `STRING(n)` = `[MarshalAs(UnmanagedType.ByValTStr, SizeConst = n + 1)]` (encodage Windows-1252 côté PLC), `LINT` horodatage = FILETIME UTC.

```iecst
TYPE E_ValueSource : (None := 0, Const := 1, Var := 2, PartData := 3, OpParam := 4, NamedPos := 5,
                      Speed := 6, Text := 7, ObjValue := 8, System := 9, MoverData := 10) USINT; END_TYPE

TYPE ST_Value :                  // 11 octets
STRUCT
    eSource : E_ValueSource;
    nIndex  : INT;               // n° de variable, donnée, paramètre, position, vitesse, texte, objet, variable système
    fValue  : LREAL;             // constante ; ObjValue : n° de valeur E1..E4 ;
                                 // position (NamedPos, Var de type position) : décalage ajouté
                                 // à la composante utilisée par l'axe (hauteur d'approche)
END_STRUCT END_TYPE

// MoverData : rang de la variable de mover (aMoverVar), valeur dans ST_MoverCtx.aVar / aVarPos (05 § 5.8).
TYPE E_BindTarget : (None := 0, Var := 1, PartData := 2, PartId := 3, Verdict := 4, PartRemoved := 5, MoverData := 6) USINT; END_TYPE
TYPE ST_Bind : STRUCT eTarget : E_BindTarget; nIndex : INT; END_STRUCT END_TYPE

TYPE E_Op : (Nop := 0, Action := 1, WaitCond := 2, WaitTime := 3, Jump := 4, JumpIfFalse := 5,
             Fork := 6, Join := 7, LoopInit := 8, LoopNext := 9, Call := 10, Return := 11,
             SetVar := 12, Alarm := 13, End := 14, StepBegin := 15, StepEnd := 16,
             WaitMover := 17, SetOut := 23, ResetMoverVars := 22, Prompt := 18, SavePos := 19, CalibStation := 21, Route := 20) UINT; END_TYPE

TYPE E_FailPolicy : (StopStation := 0, Retry := 1, PartBad := 2, Goto := 3, Ignore := 4) USINT; END_TYPE

TYPE ST_Instr :                  // 154 octets
STRUCT
    eOp         : E_Op;
    nStepId     : UDINT;         // id de la carte (ou du bloc) dans l'éditeur ; 0 = instruction technique
    nObject     : UINT;          // Action : n° local de l'objet
    nAction     : UINT;          // Action : n° d'action du catalogue
    aParam      : ARRAY[1..8] OF ST_Value;
    aBind       : ARRAY[1..8] OF ST_Bind;
    aArg        : ARRAY[1..6] OF DINT;   // arguments selon l'opération (§ 3)
    nTimeoutMs  : UDINT;         // 0 = défaut (configuration de l'objet, puis catalogue)
    eOnFail     : E_FailPolicy;
    nRetries    : USINT;
    nFailTarget : INT;           // Goto : index d'instruction
END_STRUCT END_TYPE

TYPE E_TableKind : (Operation := 0, Background := 1, Init := 2, Stop := 3, Clear := 4, Sub := 5, Calibration := 6,
                    General := 10, GeneralSub := 11, MachineCalibration := 12) USINT; END_TYPE

TYPE ST_TableDir :
STRUCT
    bUsed        : BOOL;
    sName        : STRING(40);
    eKind        : E_TableKind;
    nStart       : INT;          // index de la première instruction dans aInstr
    nLength      : INT;
END_STRUCT END_TYPE

TYPE ST_NamedPos : STRUCT stPos : ST_Position; fApproach : LREAL; bTaught : BOOL; END_STRUCT END_TYPE
TYPE E_VarType : (Number := 0, Bool := 1, Position := 2) USINT; END_TYPE
TYPE ST_VarInit : STRUCT eType : E_VarType; fValue : LREAL; stPos : ST_Position; bRetain : BOOL; END_STRUCT END_TYPE   // bRetain : persistante, conservée au redéploiement par identifiant
TYPE E_CalibState : (Never := 0, Valid := 1, Expired := 2, Failed := 3, Running := 4) USINT; END_TYPE
TYPE ST_CalibStatus : STRUCT eState : E_CalibState; tLast : LINT; sUser : STRING(24); nValidityDays : UINT; END_STRUCT END_TYPE   // 0 jour = n'expire jamais

TYPE ST_Interlock :
STRUCT
    nObject     : UINT;          // objet protégé
    nActionMask : UDINT;         // bit n-1 = action n ; 0 = toutes les actions de mouvement
    nCond       : INT;           // condition qui doit rester vraie
END_STRUCT END_TYPE
```

## 3. Arguments par opération

| Opération | `nObject`, `nAction` | `aParam` | `aBind` | `aArg` |
| --- | --- | --- | --- | --- |
| `Action` | objet, action | paramètres a1..a8 | résultats r1..r8 → cibles | — |
| `WaitCond` | — | — | — | [1] n° de condition |
| `WaitTime` | — | [1] durée (ms) | — | — |
| `Jump` | — | — | — | [1] cible |
| `JumpIfFalse` | — | — | — | [1] n° de condition, [2] cible si fausse |
| `Fork` | — | — | — | [1] index du Join, [2..5] début des branches 1..4 (0 = absente) |
| `Join` | — | — | — | [1] index du Fork |
| `LoopInit` | — | [1] nombre | — | [1] variable compteur |
| `LoopNext` | — | — | — | [1] variable compteur, [2] début de boucle |
| `Call` | — | — | — | [1] n° de table |
| `Return` | — | — | — | — |
| `SetVar` | — | [1] valeur | [1] cible | [1] 0 affecter, 1 ajouter, 2 soustraire |
| `Alarm` | — | — | — | [1] n° de texte, [2] gravité (1 avertissement, 2 défaut station, 3 défaut machine), [3] n° d'alarme de process (0..99, code 900 + n) |
| `End` | — | — | — | [1] 0 fin, 1 reprendre au début |
| `SetOut` | — | [1] valeur | — | [1] rang de la sortie (opération : sortie de la réponse ; sous-process : emplacement interne). Valeur de type position : copie de la position |
| `ResetMoverVars` | — | — | — | [1] rang de la variable de mover, 0 = toutes celles dont la règle n'est pas `Never` (refusée dans le cycle de fond, l'initialisation, l'arrêt et le vidage) |
| `Prompt` | — | — | [1] cible de la valeur saisie (mode saisie) | [1] n° de texte, [2] 0 confirmation, 1 saisie |
| `SavePos` | objet (position actuelle) | — | — | [1] n° de position nommée (calibration seulement) |
| `CalibStation` | — | — | — | [1] n° de station, [2] n° de calibration (calibration machine seulement) |
| `WaitMover` | — | — | — | — (une seule par table d'opération, au premier niveau ; avant elle, aucune source ni cible `PartData`, `PartId`, `Verdict`, `PartRemoved`) |
| `StepBegin`, `StepEnd` | — | — | — | [1] niveau (1 carte, 2 étape interne d'une fonction de module) |
| `Route` | — | paramètres d'opération 1..8 | liaisons des sorties de l'opération, rang `k` = sortie `k` (cibles : donnée de pièce, variable de mover, verdict) | [1] fonction, [2] n° d'opération, [3] emplacement (0 = tous) |

Les index d'instruction (`cible`, `début`, `Fork`, `Join`) sont **absolus** dans le tableau `aInstr` de la station (ou du process général).

## 4. Blocs de données

```iecst
TYPE ST_StationData :
STRUCT
    nObjCount  : UINT;
    aObj       : ARRAY[1..64] OF ST_ObjConfig;      // triés : parents avant enfants
    aTable     : ARRAY[1..16] OF ST_TableDir;
    nInstrCount: INT;
    aInstr     : ARRAY[1..512] OF ST_Instr;
    aCond      : ARRAY[1..64] OF ST_Condition;
    aText      : ARRAY[1..32] OF STRING(80);
    aPos       : ARRAY[1..64] OF ST_NamedPos;
    aVar       : ARRAY[1..64] OF ST_VarInit;
    aInterlock : ARRAY[1..32] OF ST_Interlock;
    nOpMapCount: USINT;
    aOpMap     : ARRAY[1..32] OF ST_OpMap;          // (fonction, opération) → table
END_STRUCT END_TYPE

TYPE ST_OpMap :              // une station peut implémenter des opérations de plusieurs fonctions
STRUCT
    nGroup       : USINT;        // fonction
    nOperationNo : USINT;        // n° de l'opération dans la fonction (1..8)
    nTable       : USINT;        // table de type Operation qui l'implémente
    bPrepare     : BOOL;         // la table contient WaitMover : demande posée à la réservation (05 § 4.1)
END_STRUCT END_TYPE

TYPE ST_GroupCfg :
STRUCT
    bUsed        : BOOL;
    sName        : STRING(40);
    eRole        : (Entry := 0, Work := 1, Exit := 2, Reject := 3) USINT;
    nStationMask : UINT;          // bit n-1 = station n ; une station peut être dans plusieurs fonctions
    nOperations  : USINT;         // nombre d'opérations de la fonction
    nPriority    : USINT;         // 1..9, ordre de service des files des stations partagées
END_STRUCT END_TYPE

TYPE ST_StationCfg :
STRUCT
    bUsed        : BOOL;
    sName        : STRING(40);
    nGroupMask   : UINT;          // bit n-1 = fonction n (redondant avec nStationMask, vérifié au chargement)
    nWorkPos     : INT;           // index dans aPlanarPos
    aQueuePos    : ARRAY[1..4] OF INT;    // 0 = absente
    fOverride    : LREAL;
END_STRUCT END_TYPE

TYPE ST_PlanarPos : STRUCT stPos : ST_Position; eKind : (Work, Queue, Buffer, Park) USINT; nStation : USINT; END_STRUCT END_TYPE

TYPE E_MoverVarReset : (Never := 0, OnDeploy := 1, OnEmpty := 2) USINT; END_TYPE
TYPE ST_MoverVarDef :
STRUCT
    nId        : UDINT;           // identifiant stable (conservé au renommage et au déplacement)
    sName      : STRING(24);
    eType      : E_VarType;
    sUnit      : STRING(8);
    fInit      : LREAL;
    stInitPos  : ST_Position;
    eReset     : E_MoverVarReset;
END_STRUCT END_TYPE

TYPE ST_MachineData :
STRUCT
    sMachineName : STRING(40);
    sProjectId   : STRING(40);    // identifiant du projet HMI
    sProjectHash : STRING(64);    // empreinte du projet déployé (SHA-256 hexadécimal)
    tDeployed    : LINT;
    sDeployedBy  : STRING(40);
    nMovers      : USINT;
    nSlots       : USINT;
    fOverride    : LREAL;
    nRejectGroup : USINT;         // 0 = aucune fonction de rebut
    nNoStationMs : UDINT;         // délai avant « mover à vider »
    nMoverVar    : USINT;
    aMoverVar    : ARRAY[1..16] OF ST_MoverVarDef;   // variables de mover (05 § 5.8)
    aGroup       : ARRAY[1..16] OF ST_GroupCfg;
    aStation     : ARRAY[1..16] OF ST_StationCfg;
    aPlanarPos   : ARRAY[1..128] OF ST_PlanarPos;
    nParkPos     : INT;
    nMachineObj  : UINT;
    aMachineObj  : ARRAY[1..32] OF ST_ObjConfig;   // planaire, movers, balise machine, sécurités
    aTable       : ARRAY[1..16] OF ST_TableDir;    // process général et sous-process
    nInstrCount  : INT;
    aInstr       : ARRAY[1..512] OF ST_Instr;
    aCond        : ARRAY[1..64] OF ST_Condition;
    aText        : ARRAY[1..32] OF STRING(80);
    stQuality    : ST_QualityCfg;  // règles par défaut, surcharges par station et fonction, imputation, budget OF (10 § 10)
END_STRUCT END_TYPE

TYPE ST_PressSegment : STRUCT eTarget : (Stroke, Force) USINT; fTarget, fSpeedPct, fForceMin, fForceMax, fStrokeMin, fStrokeMax : LREAL; END_STRUCT END_TYPE
TYPE ST_PressRecipe : STRUCT bUsed : BOOL; nStation : USINT; nObject : UINT; nRecipeNo : USINT; nSegments : USINT; aSeg : ARRAY[1..8] OF ST_PressSegment; END_STRUCT END_TYPE
```

Les noms des objets, des positions, des variables et des données de pièce ne servent qu'à l'affichage ; le PLC ne travaille qu'avec des numéros. La HMI garde la correspondance numéro → nom dans le projet déployé.

## 5. Compilation des process

Le compilateur (HMI, `StdMachine.Core`) transforme l'arbre d'un process en instructions. Règles :

### 5.1 Cartes

| Carte | Instructions générées |
| --- | --- |
| Action | `StepBegin(1)`, `Action`, `StepEnd(1)` |
| Fonction de module | `StepBegin(1)`, puis le corps de la fonction déplié (ses étapes internes encadrées par `StepBegin(2)`/`StepEnd(2)`, ses blocs compilés selon le § 5.2, ses paramètres substitués), `StepEnd(1)` |
| Mover requis | `WaitMover` (sans `StepBegin`/`StepEnd`) ; le compilateur met `bPrepare` de l'opération dans `aOpMap` |
| Demander à l'opérateur, Enregistrer la position | `StepBegin(1)`, `Prompt` ou `SavePos`, `StepEnd(1)` |
| Calibrer la station | `StepBegin(1)`, `CalibStation`, `StepEnd(1)` |
| Attendre condition | `StepBegin(1)`, `WaitCond(c)`, `StepEnd(1)` |
| Attendre durée | `StepBegin(1)`, `WaitTime(d)`, `StepEnd(1)` |
| Appeler sous-process | `StepBegin(1)`, `Call(t)`, `StepEnd(1)` ; pour chaque sortie rangée : `SetVar` de la variable cible depuis l'emplacement interne, avant `StepEnd(1)` |
| Définir la sortie | `StepBegin(1)`, `SetOut`, `StepEnd(1)` |
| Fonction personnalisée avec sorties | comme une fonction de module : corps déplié, la sortie `k` est remplacée par la variable choisie à l'appel (aucune instruction supplémentaire) |
| Réinitialiser les variables du mover | `StepBegin(1)`, `ResetMoverVars`, `StepEnd(1)` |
| Définir variable / donnée de pièce / variable de mover | `StepBegin(1)`, `SetVar`, `StepEnd(1)` |
| Signaler défaut | `StepBegin(1)`, `Alarm`, `StepEnd(1)` |
| Fin | `End(0)` |
| Opération (niveau machine) | `Route` (pas de marqueur : pas de pas à pas au niveau machine) |

### 5.2 Blocs

**Si / Sinon** :
```
        JumpIfFalse(c, SINON)
        … branche Alors …
        Jump(FIN)
SINON:  … branche Sinon …
FIN:
```
(Sans branche Sinon : `JumpIfFalse(c, FIN)` et pas de `Jump`.)

**En parallèle** (n branches) :
```
F:      Fork(J, B1, B2, …, Bn)
B1:     … branche 1 … ; Jump(J)
B2:     … branche 2 … ; Jump(J)
…
Bn:     … branche n …            (tombe sur J)
J:      Join(F)
```

**Répéter N fois** (variable compteur `t` cachée) :
```
        LoopInit(t, N)
L:      … corps …
        LoopNext(t, L)
```

**Répéter tant que c (maximum M)** :
```
        LoopInit(t, M)
L:      JumpIfFalse(c, FIN)
        … corps …
        LoopNext(t, L)
        Alarm(« Nombre maximal de répétitions atteint », défaut station, n° 5)
FIN:
```

### 5.3 Fin de table

Le compilateur termine chaque table : opération, initialisation, arrêt, vidage → `End(0)` ; cycle de fond → `End(1)` ; sous-process → `Return`. Le process général se termine par `End(0)` (le mover reprend à l'étape 1).

### 5.4 Allocation

- **Variables de process** : placées juste après les variables de station, une place chacune (deux process d'une station peuvent s'exécuter en même temps, donc pas de partage de place). Pour le PLC, ce sont des variables de station ordinaires : le jeu d'instructions ne change pas.
- **Emplacements de sorties de sous-process** : un par sortie de chaque sous-process à sorties appelé par la station, pris juste avant les compteurs de boucle (en descendant depuis 64) ; le validateur compte tout dans les 64.
- **Variables cachées** (compteurs de boucle) : prises en partant de la variable 64 vers le bas ; le validateur vérifie que variables de station + de process + cachées ≤ 64.
- **Conditions** : dédoublonnées par contenu, numérotées dans l'ordre de première apparition.
- **Textes** : messages d'alarme de process et constantes texte, dédoublonnés.
- **Objets** : n° local = rang dans le parcours en largeur de l'arborescence de la station (parents avant enfants), stable tant que l'arborescence ne change pas.
- **Opérations** : le n° d'une opération (1..8) est son rang dans la liste des opérations de sa fonction. Chaque station membre reçoit dans `aOpMap` un couple (fonction, n° d'opération) → table pour chaque opération de chacune de ses fonctions ; deux couples peuvent pointer vers la même table.
- **Verrouillages** : ceux saisis par l'utilisateur, puis ceux générés par les modules (robot cartésien).

### 5.5 Traçabilité de la compilation

Le compilateur produit, à côté des données PLC, une **carte de compilation** : pour chaque instruction, la carte d'origine (`nStepId`), le chemin dans l'arbre (process › bloc › branche › carte › étape interne de fonction), et l'inverse. La HMI s'en sert pour surligner en direct la carte qui s'exécute à partir du PC publié par le PLC, et pour afficher « étape en cours » et « étape suivante ».

### 5.6 Exemple : opération « Presser pignon » compilée

Objets de la station (n° locaux) : 1 Robot XYZC (module), 2 Vision Asyril, 3 Chargeur vibrant, 4 Presse, 5 Présence nid, 6 Compteur rebuts, 7 Axe X, 8 Axe Y, 9 Axe Z, 10 Axe C, 11 Aspiration. Positions : 1 Repos, 2 Zone alimenteur, 3 Dépose nid, 4 Pose presse, 5 Mover, 6 Bac rebut. Variables : 1 PosPignon (position), 2 JugementPresse. Données de pièce : 1 Verdict, 2 Force finale. Paramètre d'opération 1 : recette de presse.

```
Table 1 « Presser pignon » (Operation ; aOpMap : fonction Pressage pignon, opération 1 → table 1), instructions 1 à 96

  1  StepBegin(1)                     step=101  Chercher pièce
  2  Action  obj 2 act 3              step=101  onFail=Retry ×3
  3  StepEnd(1)                       step=101
  4  StepBegin(1)                     step=102  Obtenir pièce
  5  Action  obj 2 act 4  r1→Var1     step=102
  6  StepEnd(1)                       step=102
  7  StepBegin(1)                     step=103  Prendre pièce (robot) pos=Var1
  8    StepBegin(2)                   step=103  · Aller au-dessus
  9    Action obj 9 act 4 a1=Const(-5) a2=Speed(2)          Z → sécurité
 10    Fork(J=17, 11, 13, 15)
 11    Action obj 7 act 4 a1=Var1 a2=Speed(2)               X
 12    Jump(17)
 13    Action obj 8 act 4 a1=Var1 a2=Speed(2)               Y
 14    Jump(17)
 15    Action obj 10 act 4 a1=Var1 a2=Speed(2)              C
 16    Nop
 17    Join(10)
 18    StepEnd(2)
 19    StepBegin(2)                   step=103  · Approche
 20    Action obj 9 act 4 a1=Var1(+10) a2=Speed(2)          Z → pos.Z + approche (décalage 10 mm)
 21    Action obj 9 act 4 a1=Var1 a2=Speed(1)               Z → pos.Z (lente)
 22    StepEnd(2)
 23    StepBegin(2)                   step=103  · Saisie
 24    Action obj 11 act 1 a1=Const(1)                      Aspirer
 25    StepEnd(2)
 26    StepBegin(2)                   step=103  · Dégagement
 27    Action obj 9 act 4 a1=Const(-5) a2=Speed(2)
 28    Action obj 11 act 4 r1→(ignoré)                      Vérifier présence
 29    StepEnd(2)
 30  StepEnd(1)                       step=103
 …   (Poser pièce Dépose nid, Prendre pièce Mover, Poser pièce Pose presse : même dépliage)
 64  Fork(J=78, 65, 69, 73)           step=110  bloc En parallèle
 65    StepBegin(1)                   step=111  Exécuter recette
 66    Action obj 4 act 1 a1=OpParam1 r1→Var2 r2→PartData2
 67    StepEnd(1)
 68    Jump(78)
 69    StepBegin(1)                   step=112  Chercher pièce suivante
 70    Action obj 2 act 3             onFail=Retry ×3
 71    StepEnd(1)
 72    Jump(78)
 73    StepBegin(1)                   step=113  Aller à Repos (fonction, dépliée sur 74..77)
 …
 78  Join(64)
 79  JumpIfFalse(cond 1, 89)          step=120  bloc Si / Sinon
                                      cond 1 = All(Var2 = 1, PartData2 ≥ 800, PartData2 ≤ 900)
 80    StepBegin(1) … Prendre pièce Pose presse … StepEnd(1)          step=121
 …     Poser pièce Mover                                              step=122
 86    StepBegin(1)                   step=123  Verdict bon
 87    SetVar target=Verdict value=Const(1)
 88    StepEnd(1) ; Jump(96)
 89    … Prendre pièce Pose presse, Poser pièce Bac rebut …           step=124, 125
 93    StepBegin(1) ; Action obj 6 act 1 ; StepEnd(1)                 step=126  Compter le rebut
 95    SetVar target=Verdict value=Const(2) ; SetVar target=PartRemoved value=Const(1)   step=127  Pièce retirée
 96  End(0)
```

(Les numéros exacts dépendent du dépliage ; la HMI affiche ce listing dans le panneau « Table compilée » du studio pour le diagnostic.)

## 6. Symboles ADS — GVL_Hmi

### 6.1 Principe des handshakes

Toute commande HMI → PLC suit le même schéma :

1. La HMI remplit la structure de commande et écrit `nSeq := dernier nSeq + 1` en dernier (une seule écriture ADS de la structure entière).
2. Le PLC traite la commande une seule fois quand `nSeq` diffère du dernier traité, puis écrit la réponse avec le même `nSeq` et un résultat.
3. La HMI attend la réponse au plus 2 s (10 s pour un déploiement) ; au-delà, la commande est en échec « PLC ne répond pas ».

### 6.2 Écrits par la HMI

| Symbole | Type | Rôle |
| --- | --- | --- |
| `GVL_Hmi.nHmiHeartbeat` | UDINT | Incrémenté toutes les 500 ms. Perte > 5 s : événement « HMI déconnectée » ; la production continue ; un jog en cours s'arrête |
| `GVL_Hmi.stCmd` | `ST_CmdRequest` | Commande d'état : cible (machine ou station n), commande (Reset, Start, Stop, Hold, Abort, Clear, ainsi que `DrainStart` et `DrainCancel` pour la machine seulement, `05` § 5.7), changement de mode, utilisateur |
| `GVL_Hmi.stOverride` | `ST_OverrideRequest` | Nœud (machine, station n, objet n de station m), valeur, override manuel ou non |
| `GVL_Hmi.aManual[0..16]` | `ST_ManualRequest` | Canal manuel de la machine (0) et de chaque station |
| `GVL_Hmi.nJogAlive` | UDINT | Garde du jog, incrémentée toutes les 100 ms bouton tenu |
| `GVL_Hmi.aStep[1..16]` | `ST_StepControl` | Pas à pas par station |
| `GVL_Hmi.stLaunch` | `ST_LaunchRequest` | Maintenance : lancer une table d'une station (station, table, mover ou 0, paramètres d'opération) |
| `GVL_Hmi.stOrder` | `ST_OrderRequest` | OF : lancer (numéro interne, quantité, paramètres), suspendre, reprendre, annuler |
| `GVL_Hmi.stMoverVar` | `ST_MoverVarRequest` | Maintenance : écrire la valeur d'une variable d'un mover (mover, rang, valeur ou position) ou la remettre à sa valeur initiale ; utilisateur |
| `GVL_Hmi.stPrompt` | `ST_PromptAnswer` | Réponse de l'opérateur à une demande de calibration : confirmer, annuler, valeur saisie |
| `GVL_Hmi.stPartCmd` | `ST_PartRequest` | Confirmer le retrait des pièces d'un mover à vider |
| `GVL_Hmi.stAlarmAck` | `ST_AlarmAckRequest` | Acquitter un avertissement (objet, code ; 0 = tous) |
| `GVL_Hmi.stStaging` | `ST_Staging` | Zone d'attente du déploiement : `stMachine`, `aStation[1..16]`, `aPressRecipe[1..64]` |
| `GVL_Hmi.stDeploy` | `ST_DeployRequest` | Vérifier, activer ou annuler la zone d'attente ; CRC de chaque bloc ; utilisateur |
| `GVL_Hmi.stForceCmd` | `ST_ForceCommand` | Poser ou lever un forçage : état déclaré et démarrage bloqué, station hors production, mover hors service (`12-forcages-et-etats-declares.md` § 7) |
| `GVL_Hmi.stQualityCmd` | `ST_QualityCommand` | Libérer ou remettre à zéro une portée de la surveillance des rebuts (portée, index, motif, utilisateur) |
| `GVL_Hmi.stSimForce` | `ST_SimForceRequest` | Forcer une entrée d'un objet simulé (Maintenance) |

### 6.3 Lus par la HMI

| Symbole | Type | Fréquence de lecture HMI |
| --- | --- | --- |
| `GVL_Hmi.stMachineStatus` | état, mode, override effectif, balise, défauts, OF en cours et compteurs, horloge PLC, battement PLC, empreinte du projet actif | 10 Hz |
| `GVL_Hmi.aStationStatus[1..16]` | état, mode, override effectif, balise, opération en cours, 8 contextes (statut, table, PC, carte, carte suivante, objet, action, essai, attente) | 10 Hz |
| `GVL_Hmi.aObjStatus[0..16, 1..64]` | `ST_ObjStatus` | 10 Hz pour la station affichée, 1 Hz pour les autres |
| `GVL_Hmi.aMoverStatus[1..32]` | état, X, Y, C, station, destination, table, PC, carte, emplacements (présence, identifiant, état, verdict), variables de mover (valeur ou position, avec leur identifiant) | 10 Hz |
| `GVL_Hmi.stAlarms` | alarmes actives + tampon d'événements avec `nWriteSeq` | 4 Hz (lecture depuis le dernier `nSeq`) |
| `GVL_Hmi.stTraces` | tampon de traces avec `nWriteSeq` | 2 Hz |
| `GVL_Hmi.stForce` | forçages actifs (état déclaré, démarrage bloqué, stations hors production, movers hors service, motifs) | 2 Hz |
| `GVL_Plant` | données d'état et compteurs au format PackTags, exposées en OPC UA (`11-interface-usine.md` § 3) ; la HMI les lit aussi pour la passerelle usine | 1 Hz |
| `GVL_Hmi.stQuality` | état de toutes les portées de la surveillance des rebuts (`10-surveillance-des-rebuts.md` § 9) | 2 Hz |
| `GVL_Hmi.aCurve[1..8]` | courbes de presse | à la demande, après une trace qui en référence une |
| `GVL_Hmi.*Resp` | réponses des handshakes | après chaque commande |

La HMI lit par **lecture groupée** (ADS sum read) toutes les structures d'une même fréquence en une requête.

## 7. Interface C# — IPlcClient

La HMI ne connaît que cette interface. Deux implémentations : `SimulatedPlcClient` (projet `StdMachine.Simulation`) et, plus tard, `AdsPlcClient` (projet `StdMachine.Plc.Ads`, bibliothèque Beckhoff.TwinCAT.Ads).

```csharp
public interface IPlcClient : IAsyncDisposable
{
    PlcConnectionState ConnectionState { get; }
    event EventHandler<PlcConnectionState>? ConnectionStateChanged;

    // Instantané cohérent de tout l'état publié, mis à jour à chaque lecture cyclique.
    PlcSnapshot Snapshot { get; }
    event EventHandler<PlcSnapshot>? SnapshotUpdated;          // au plus 10 fois par seconde

    Task ConnectAsync(CancellationToken ct);

    Task<CommandResult> SendCommandAsync(StateCommand command, CancellationToken ct);
    Task<CommandResult> SetModeAsync(MachineMode mode, CancellationToken ct);
    Task<CommandResult> SetOverrideAsync(OverrideTarget target, double percent, bool manual, CancellationToken ct);

    Task<ManualResult> ExecuteManualAsync(ManualActionRequest request, CancellationToken ct);
    IJogSession StartJog(JogRequest request);                  // Dispose() = relâchement ; envoie la garde toutes les 100 ms
    Task<TeachResult> TeachPositionAsync(TeachRequest request, CancellationToken ct);

    Task<CommandResult> SetStepModeAsync(int station, StepGranularity? granularity, CancellationToken ct); // null = désactivé
    Task<CommandResult> StepAsync(int station, CancellationToken ct);
    Task<CommandResult> LaunchTableAsync(LaunchRequest request, CancellationToken ct);

    Task<DeployResult> DeployAsync(CompiledProject project, IProgress<DeployProgress>? progress, CancellationToken ct);
    Task<DeployedInfo> GetDeployedInfoAsync(CancellationToken ct);

    Task<CommandResult> StartOrderAsync(OrderStart order, CancellationToken ct);
    Task<CommandResult> OrderCommandAsync(OrderCommand command, CancellationToken ct);
    Task<CommandResult> ConfirmPartsRemovedAsync(int mover, CancellationToken ct);
    Task<CommandResult> AcknowledgeAlarmAsync(AlarmKey? key, CancellationToken ct); // null = tous les avertissements

    IAsyncEnumerable<AlarmEvent> ReadAlarmEventsAsync(CancellationToken ct);       // flux continu, sans perte ni doublon
    IAsyncEnumerable<TraceRecord> ReadTracesAsync(CancellationToken ct);
    Task<PressCurve?> ReadCurveAsync(uint curveId, CancellationToken ct);
}

public interface ISimulatorControl                              // seulement SimulatedPlcClient
{
    double TimeScale { get; set; }                              // 0,25 à 4
    void InjectObjectError(int station, int objectId, int errorCode, uint subCode = 0);
    void ForceStationAbort(int station);
    void SetSafetyCircuit(int station, int circuit, bool ok);   // station 0 = machine
    void SetInput(int station, int objectId, int signal, double value);
    void SetFailureRate(int objectType, double rate);           // par exemple 0,03 pour la presse
    void Reset(int? seed = null);
}
```

- `PlcSnapshot` est immuable (records C#) : machine, stations (avec contextes), objets, movers, alarmes actives. Les vues lisent l'instantané ; elles ne demandent jamais une valeur au PLC.
- `CompiledProject` est le résultat du compilateur : les mêmes structures que le § 4, en objets C#, plus la carte de compilation. `AdsPlcClient` les sérialise en octets ; `SimulatedPlcClient` les utilise directement.

## 8. Conformité du simulateur

Le simulateur n'est pas une maquette : c'est une implémentation C# du PLC qui doit se comporter comme lui.

- Même machine d'états (`05-plc-sequenceurs.md` § 2), même interpréteur (§ 3), mêmes séquenceurs (§ 4, 5), même gestion des movers (§ 6, avec un plan 2D simple : déplacement en ligne droite à vitesse constante, attente si la destination est occupée), même canal manuel, même pas à pas, mêmes vérifications au déploiement.
- Mêmes objets : un modèle par type, avec le comportement simulé décrit dans `03-catalogue-objets.md`, le même contrat d'action, le même calcul d'override et de timeout.
- Cycle de 10 ms (multiplié par l'échelle de temps), sur un fil dédié ; l'instantané est publié à 10 Hz.
- Les tests de l'interpréteur et des séquenceurs sont écrits sur le simulateur, puis rejoués sur le PLC (TcUnit) avec les mêmes cas.
