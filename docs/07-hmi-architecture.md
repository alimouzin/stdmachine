# Architecture de la HMI

Ce document fixe les décisions techniques de la HMI : solution, bibliothèques, modèle de données, compilateur, validation, persistance, services, infrastructure tactile, simulateur, tests et outils de vérification. Le design et les écrans sont dans `08-hmi-design-ecrans.md`.

## 1. Décisions techniques

| Sujet | Décision | Raison |
| --- | --- | --- |
| Plateforme | .NET 10 (LTS), C# 14, WPF | Windows seulement ; .NET 8 n'est plus supporté après novembre 2026, .NET 10 l'est jusqu'en novembre 2028 |
| MVVM | CommunityToolkit.Mvvm (générateurs `[ObservableProperty]`, `[RelayCommand]`) | Standard Microsoft, sans réflexion, réduit le code répétitif |
| Composition | Microsoft.Extensions.Hosting (injection de dépendances, configuration, journalisation) | Démarrage et services standard |
| Journalisation | Serilog (fichiers journaliers, 30 jours) via Serilog.Extensions.Hosting | Fichiers lisibles sur machine sans outillage |
| Projet machine | Fichier JSON (System.Text.Json avec générateur de source) | Lisible, comparable dans Git, exportable |
| Données d'exploitation | SQLite (Microsoft.Data.Sqlite, SQL écrit à la main) | OF, utilisateurs, journal, alarmes, traces ; une base locale sans serveur |
| PLC | Interface `IPlcClient` ; `SimulatedPlcClient` d'abord, `AdsPlcClient` (Beckhoff.TwinCAT.Ads) plus tard | La HMI entière tourne sans PLC |
| Tests | xUnit v3 | Standard |
| Interface | Aucune bibliothèque de contrôles tierce ; thème et contrôles maison | Maîtrise du rendu tactile et du style |
| Polices | IBM Plex Sans (licence OFL, TTF statiques 400/500/600) et Material Symbols Outlined (Apache 2.0, TTF), intégrés comme ressources ; les icônes sont affichées par leur point de code (table nom → code générée depuis le fichier `.codepoints` de Google), pas par ligature | Style de la maquette, hors ligne ; rendu des icônes indépendant du support des ligatures par WPF |
| Claviers | Pavé numérique et clavier AZERTY intégrés à l'application | Le clavier tactile de Windows est peu fiable sur les PC industriels |
| Interface usine | Première version : contrats, modèle des messages, canal Journal de messages (fichiers JSON) et page Communications. Plus tard : MQTT par MQTTnet (licence MIT) dans la HMI pour les événements, OPC UA par le serveur TwinCAT (TF6100) côté PLC pour l'état en direct | Préparer l'intégration sans dépendre d'un broker ; standards de l'industrie ensuite |

Tout autre paquet NuGet doit être justifié avant d'être ajouté.

## 2. Solution

```
StdMachine.sln
├─ src/
│  ├─ StdMachine.Core           (net10.0)  domaine, catalogue, projet, compilateur, validation,
│  │                                       droits, overrides, IPlcClient et instantanés, IOrderSource
│  ├─ StdMachine.Simulation     (net10.0)  SimulatedPlcClient, objets simulés, interpréteur,
│  │                                       séquenceurs, movers, ISimulatorControl
│  ├─ StdMachine.Persistence    (net10.0)  projet JSON, base SQLite, dépôts
│  ├─ StdMachine.Hmi            (net10.0-windows, WPF)  application, vues, vues-modèles,
│  │                                       thème, contrôles, services d'interface
│  ├─ StdMachine.Plant          (net10.0)  passerelle usine : contrats des canaux, messages,
│  │                                       schémas JSON, canal Journal de messages (MQTT plus tard)
│  └─ StdMachine.Plc.Ads        (plus tard) AdsPlcClient
├─ tests/
│  ├─ StdMachine.Core.Tests
│  ├─ StdMachine.Simulation.Tests
│  ├─ StdMachine.Persistence.Tests
│  └─ StdMachine.Plant.Tests
├─ catalog/            catalog.json (types d'objets et de modules), subalarms/*.xml
├─ samples/            MX-4.stdm.json (machine d'exemple de 01-definition-machine.md)
├─ assets/fonts/       IBM Plex Sans, Material Symbols Outlined
└─ docs/
```

Règle de dépendance : `Core` ne dépend de rien ; `Simulation` et `Persistence` dépendent de `Core` ; `Hmi` dépend de tous ; aucun projet ne dépend de `Hmi`.

## 3. Modèle du domaine (StdMachine.Core)

### 3.1 Projet machine

```
MachineProject
├─ Id (Guid), Name, SchemaVersion, CatalogVersion, CreatedAt, ModifiedAt
├─ Machine : MachineConfig
│  ├─ Planar : tuiles (colonnes, lignes, taille), nombre de movers, emplacements par mover, vitesse par défaut
│  ├─ PlanarPositions : PlanarPosition[] (Id, Kind Work|Queue|Buffer|Park, Position, StationId?)
│  ├─ Groups : GroupConfig[] (Id, Name, Role, Priority, StationIds, Operations : OperationSignature[] (Name, Parameters, Outputs : OutputDef[≤8]))
│  │            — « fonctions » dans l'interface ; une station peut figurer dans plusieurs fonctions
│  ├─ RejectGroupId?, NoStationDelayMs
│  ├─ MachineObjects : ObjectNode[] (planaire, movers, balise, sécurités)
│  ├─ PartData : PartDataDef[] (Id, Name, Type, Unit) — Verdict et Identifiant réservés
│  ├─ MoverVars : MoverVariable[≤16] (Id (stable), Name, Type, Unit, Initial, Reset Never|OnDeploy|OnEmpty)
│  ├─ IoImage : IoChannel[] (Kind DI|DO|AI|AO, Index, Name, Terminal)   (import CSV)
│  ├─ Quality : QualityConfig (règles par défaut par portée, surcharges par station et par fonction,
│  │            imputation par fonction, budget de rebuts d'OF par défaut)
│  └─ HardwarePools : HardwareItem[] (Kind Axis|EtherCat|Network|Planar, Index, Name)
├─ FunctionLibrary : LibraryFunction[] (Id, Name, Description, ModuleTypeId, BasedOn, Params, Outputs : OutputDef[≤8], Body : Sequence ; objets désignés par rôle de module : ObjectRef = (Role))
├─ Stations : StationConfig[]
│  ├─ Id, Name, Number (1..16), OverrideDefault, Layout (position sur le plan)
│  │   (ses fonctions se déduisent de Groups.StationIds ; son rôle est celui de ses fonctions)
│  ├─ Objects : ObjectNode (arbre : Id, TypeId, Name, ChildRole?, Simulated, Disabled, Override,
│  │            Params[], Signals[], Device, Speeds[], Timeouts[], Children[], Layout (vue de dessus))
│  ├─ Positions : NamedPosition[] (Id, Name, X, Y, Z, C, Frame, Approach?, Taught, TaughtAt, TaughtBy)
│  ├─ Variables : StationVariable[] (Id, Name, Type, Initial, Retained)   — partagées par tous les process
│  │   (ProcessDefinition.Variables : ProcessVariable[] (Id, Name, Type, Initial), visibles dans ce seul process)
│  ├─ Interlocks : Interlock[] (Id, ObjectId, Actions | AllMotion, Condition, Generated)
│  ├─ PressRecipes : PressRecipe[]
│  ├─ CustomFunctions : FunctionDefinition[] (fonctions locales à la station, héritées d'anciens projets ; à publier dans FunctionLibrary ; Outputs : OutputDef[≤8])
│  └─ Processes : ProcessDefinition[] (Kind, Name, Implements : OperationRef[] (GroupId, OperationNo), Parameters, Body)
├─ GeneralProcess : ProcessDefinition (+ sous-process généraux)
└─ Deployed : DeployedSnapshot? (empreinte, date, auteur, copie du projet compilé)
```

- Identifiants : `Guid` pour tous les éléments du projet (stables aux renommages). Les numéros PLC (n° local d'objet, n° de station, n° de condition, `StepId`) sont calculés par le compilateur. `StepId` est un entier 32 bits stable dérivé du `Guid` de la carte et enregistré dans le projet à sa création.
- Les valeurs de paramètres sont des `ValueRef` : `Const(double)`, `Var(Guid)`, `PartData(Guid)`, `OpParam(int)`, `NamedPos(Guid, offset)`, `Speed(int | percent)`, `Text(string)`, `ObjValue(Guid, int)`, `System(SystemVar)`.

### 3.2 Process

```
ProcessDefinition : Kind (Operation|Background|Init|Stop|Clear|Calibration|Sub|General|GeneralSub|MachineCalibration), ValidityDays? (calibrations), Name, Implements (Operation : opérations de fonctions implémentées), Parameters, Outputs : OutputDef[≤8] (sous-process), Variables : ProcessVariable[], Body : Sequence
Sequence : Step[]
Step (abstrait) : Id (Guid), StepId (uint), Label?, Comment?
├─ ActionStep : ObjectId, ActionNo, Args : ValueRef[], Bindings : Binding[] (sorties : cible Var = variable de station ou de process, PartData, MoverVar, PartId, Verdict, PartRemoved), TimeoutMs?, FailPolicy
├─ FunctionStep : ModuleObjectId, FunctionRef (module | bibliothèque | personnalisée de station), Args, OutputBindings : Binding[] (personnalisée), FailPolicy
├─ WaitConditionStep : Condition, TimeoutMs?
├─ WaitTimeStep : DurationMs (ValueRef)
├─ CallStep : ProcessId, OutputBindings : Binding[]
├─ SetOutputStep : OutputId, Value : ValueRef                 (bloc composé ayant des sorties)
├─ SetStep : Target (Var | PartData | MoverVar | Verdict | PartRemoved), Operation (= | += | −=), Value
├─ PromptStep : Text, Mode (Confirm|Input), Target?     (calibration)
├─ SavePositionStep : ObjectId, PositionId               (calibration)
├─ CalibrateStationStep : StationId, CalibrationId, FailPolicy   (calibration machine)
├─ ResetMoverVarsStep : VariableId? (null = toutes celles dont la règle n'est pas Never)
├─ WaitMoverStep                                              (séparateur préparation / avec mover)
├─ AlarmStep : Message, Severity, AlarmNo
├─ EndStep
├─ RouteStep : GroupId, OperationNo, Slot, Args, OutputBindings : Binding[]   (niveau machine)
└─ Block (abstrait)
   ├─ ParallelBlock : Branches : Sequence[2..4]
   ├─ IfBlock : Condition, Then : Sequence, Else : Sequence, ElseLabel?
   └─ RepeatBlock : Mode (Count|While), Count | Condition + Max, Body : Sequence
OutputDef : Id, Name? (sans nom : « Sortie <rang> »), Type (Number|Integer|Bool|Verdict|Position), Unit
Binding : cible Var | PartData | MoverVar | PartId | Verdict | PartRemoved | Output (sortie du bloc en cours)
Condition : Combine (All|Any), Terms : CondTerm[1..4] (Left : ValueRef, Cmp, Right : ValueRef)
FailPolicy : Kind (StopStation|Retry|PartBad|Goto|Ignore), Retries, GotoStepId
```

### 3.3 Catalogue

`catalog/catalog.json` décrit les types de `03-catalogue-objets.md`. Extrait :

```json
{
  "catalogVersion": 1,
  "types": [
    {
      "id": 10, "code": "Cylinder", "name": "Vérin", "category": "Mouvement", "icon": "swap_vert",
      "hasMotion": true, "needsInit": false,
      "signals": [
        { "no": 1, "name": "Commande travail", "kind": "DO", "required": true },
        { "no": 2, "name": "Commande repos", "kind": "DO", "required": false, "visibleWhen": "P1 == 1" },
        { "no": 3, "name": "Fin de course travail", "kind": "DI", "required": false },
        { "no": 4, "name": "Fin de course repos", "kind": "DI", "required": false }
      ],
      "params": [
        { "no": 1, "name": "Commande", "type": "Enum", "values": ["Monostable", "Bistable"], "default": 0 },
        { "no": 2, "name": "Temps de stabilisation", "type": "Duration", "default": 50, "min": 0, "max": 5000 },
        { "no": 3, "name": "Temps de déplacement sans capteur", "type": "Duration", "default": 500, "min": 10, "max": 30000 },
        { "no": 4, "name": "Position sûre sur Abort", "type": "Enum", "values": ["Repos", "Travail", "Maintien"], "default": 0 },
        { "no": 5, "name": "Ordre d'initialisation", "type": "Int", "default": 1, "min": 0, "max": 9 }
      ],
      "display": [ { "no": 1, "name": "Position", "type": "Enum", "values": ["Inconnue", "Repos", "Travail", "Intermédiaire", "Incohérente"] } ],
      "actions": [
        { "no": 1, "name": "Move travail", "icon": "south", "motion": true, "timeoutMs": 2000, "params": [], "results": [] },
        { "no": 2, "name": "Move repos", "icon": "north", "motion": true, "timeoutMs": 2000, "params": [], "results": [] },
        { "no": 3, "name": "Lire position", "immediate": true, "params": [],
          "results": [ { "no": 1, "name": "Position", "type": "Enum", "values": ["Inconnue", "Repos", "Travail", "Intermédiaire", "Incohérente"] } ] }
      ],
      "errors": [
        { "code": 100, "text": "Les deux capteurs actifs" },
        { "code": 101, "text": "Perte du capteur de la position atteinte hors mouvement" },
        { "code": 102, "text": "Vérin bloqué : capteur de départ toujours actif" }
      ],
      "simulation": { "moveMs": "P3|300" }
    }
  ],
  "modules": [
    {
      "id": 101, "code": "CartesianRobot", "name": "Robot cartésien XYZC", "icon": "precision_manufacturing",
      "roles": [
        { "role": "AxisX", "types": [11], "component": "X", "required": true },
        { "role": "AxisY", "types": [11], "component": "Y", "required": true },
        { "role": "AxisZ", "types": [11], "component": "Z", "required": true },
        { "role": "AxisC", "types": [12], "required": false },
        { "role": "Tool", "types": [20, 21], "required": true }
      ],
      "params": [ { "no": 1, "name": "Hauteur Z de sécurité", "type": "Real", "unit": "mm", "default": -5 } ],
      "generatedInterlocks": [ { "roles": ["AxisX", "AxisY", "AxisC"], "allMotion": true,
                                 "condition": "ObjValue(AxisZ,1) >= P1 - AxisZ.P4" } ],
      "functions": [ { "id": "MoveTo", "name": "Aller à", "params": ["pos:Position", "v:Speed=2"], "body": [ "…arbre d'étapes sur les rôles…" ] } ]
    }
  ],
  "commonErrors": [ { "code": 1, "text": "Timeout de l'action" } ]
}
```

- Le corps des fonctions de module est un arbre d'étapes (même format que les process) qui référence des rôles (`AxisZ`) et des paramètres (`pos`, `appr`) au lieu d'objets et de valeurs.
- Un test vérifie le catalogue à chaque compilation de la solution : numéros uniques, types de valeurs connus, fonctions qui ne référencent que des rôles et des actions existants.
- Outil `Système › Exporter les constantes PLC` : génère `GVL_Catalog.TcGVL` (constantes de types, d'actions, d'erreurs) depuis le catalogue.

## 4. Compilateur et validation

### 4.1 Chaîne

```
Projet → Validation (règles) → Résolution (numéros, positions, vitesses, conditions, textes)
       → Dépliage des fonctions de module → Génération des instructions (06 § 5)
       → Vérification des limites → CompiledProject + carte de compilation + listing
```

- La compilation est **pure** (pas d'effet de bord) et rapide (< 200 ms pour la machine d'exemple) : elle tourne à chaque modification, en arrière-plan avec un délai de 300 ms, pour alimenter la barre de validation et la vue « Table compilée ».
- Le résultat d'une compilation porte une empreinte SHA-256 du projet compilé, comparée à celle publiée par le PLC pour savoir si le PLC a le projet courant.

### 4.2 Règles de validation

Chaque problème a un code, une gravité, un message, et un **lien** vers l'élément en cause (page, élément sélectionné, champ en surbrillance).

| Code | Gravité | Règle |
| --- | --- | --- |
| V001 | Erreur | Une position nommée utilisée n'est pas apprise |
| V002 | Erreur | Un signal obligatoire d'un objet utilisé n'est pas raccordé |
| V003 | Erreur | Deux objets écrivent la même sortie, ou utilisent le même élément matériel |
| V004 | Erreur | Un objet est utilisé dans deux branches d'un même bloc En parallèle |
| V005 | Erreur | Paramètre hors bornes ou de mauvais type |
| V006 | Erreur | Une station membre d'une fonction n'implémente pas une des opérations de cette fonction |
| V007 | Erreur | Limite dépassée (instructions, contextes, objets, variables, conditions, positions) — le message donne la valeur et la limite |
| V008 | Erreur | Condition incomplète (terme sans valeur) |
| V009 | Erreur | Sous-process appelé introuvable, ou récursion |
| V010 | Erreur | Rôle obligatoire d'un module vide |
| V011 | Erreur | Une étape utilise un objet désactivé |
| V012 | Erreur | Opération appelée par le process général avec des paramètres qui ne correspondent pas à sa signature |
| V013 | Erreur | Deux stations ont le même n°, ou une station n'a pas de position de travail planaire |
| V014 | Erreur | Résultat lié à une cible de type incompatible |
| V015 | Erreur | Fonction utilisée par le process général sans aucune station |
| V016 | Erreur | Cible d'une politique d'échec « Aller à l'étape » hors de la branche de l'étape |
| V017 | Erreur | Une opération d'une fonction est implémentée par deux process de la même station, ou un process implémente des opérations dont les paramètres diffèrent |
| V018 | Erreur | Une station appartient à plus de 8 fonctions, ou a plus de 32 couples (fonction, opération) |
| V019 | Erreur | Règle qualité incohérente (avertissement ≥ action, échantillon minimal > fenêtre, fenêtre hors 1..255) |
| V020 | Erreur | Une variable de process est utilisée dans un autre process que le sien |
| V021 | Erreur | Étape Mover requis dans un bloc, un sous-process, un process qui n'est pas une opération, ou en plusieurs exemplaires |
| V022 | Erreur | Une étape avant Mover requis lit ou écrit une donnée de pièce, le verdict, l'identifiant ou l'emplacement |
| V023 | Erreur | Une variable persistante est écrite ailleurs que dans une calibration |
| V024 | Erreur | Étape Demander à l'opérateur, Enregistrer la position ou Calibrer la station dans un process qui n'est pas une calibration (ou Calibrer la station hors d'une calibration machine) |
| V025 | Erreur | Calibrer la station vise une station ou une calibration introuvable, ou une calibration qui s'appelle elle-même |
| V026 | Erreur | Variable de mover (ou étape Réinitialiser) utilisée dans le cycle de fond, l'initialisation, l'arrêt ou le vidage, ou dans un sous-process appelé par eux |
| V027 | Erreur | Déclaration des variables de mover invalide : nom en double ou vide, valeur initiale hors type, plus de 16 variables |
| V028 | Erreur | Sous-process à sorties appelé depuis deux contextes qui peuvent tourner en même temps (deux branches d'un même En parallèle, ou l'opération et le cycle de fond d'une station) |
| V029 | Erreur | Étape Définir la sortie ou liaison vers une sortie hors d'un bloc qui en déclare, sortie inexistante, ou déclaration invalide (plus de 8 sorties, nom en double, deux sorties de type position ou une sortie texte pour une opération) |
| V030 | Erreur | Fonction de bibliothèque appelée sur un module d'un autre type que le sien |
| V031 | Erreur | Corps d'une fonction de bibliothèque qui désigne un objet par instance, ou emploie une variable ou une position nommée de station, ou un rôle absent du type de module |
| V101 | Avertissement | Objet en simulation |
| V102 | Avertissement | Recette de presse non vérifiée sur la presse |
| V103 | Avertissement | Aucun chemin ne traite le verdict mauvais et aucune fonction de rebut n'existe |
| V104 | Avertissement | Un chemin du process général arrive à Fin sans passer par une sortie |
| V105 | Avertissement | Seuils de vide trop proches (< 10 kPa) |
| V106 | Avertissement | Timeout plus court que le temps de mouvement estimé |
| V107 | Avertissement | Plus de movers que de places (travail + files + tampons − 1) : blocage possible |
| V108 | Avertissement | Position nommée du robot au-dessus de la hauteur de sécurité |
| V109 | Avertissement | Objet déclaré mais jamais utilisé |
| V110 | Avertissement | Étape Chercher pièce sans politique Refaire |
| V111 | Avertissement | Station membre d'aucune fonction (elle ne sera jamais utilisée en production) |
| V112 | Avertissement | Station partagée par plusieurs fonctions sans position de file, et moins de 2 positions tampon : blocage possible |
| V113 | Avertissement | Fonction qui impute ses rebuts sans fonction amont de rôle Travail |
| V114 | Avertissement | Arrêt qualité réglé sur une station seule dans une de ses fonctions (sa pause bloque la fonction) |
| V115 | Avertissement | Variable de process lue sur un chemin où elle n'est pas écrite avant (elle garde la valeur du passage précédent) |
| V116 | Avertissement | Étape Mover requis sans aucune étape avant elle (elle n'apporte rien) ou en première position après des étapes qui ne servent qu'à attendre |
| V117 | Avertissement | Une variable de mover est écrite dans deux branches d'un même bloc En parallèle (la dernière écriture gagne) |
| V118 | Avertissement | Variable de mover lue mais écrite par aucun process ni calibration (elle garde toujours sa valeur initiale), sauf règle « Jamais » |
| V119 | Avertissement | Variable de mover déclarée mais jamais utilisée |
| V121 | Avertissement | Sortie d'un bloc composé non définie sur tous les chemins (elle garde la valeur du passage précédent) |
| V201 | Information | Le PLC n'a pas le projet courant (déploiement nécessaire) |

## 5. Persistance (StdMachine.Persistence)

### 5.1 Projet

- Dossier de travail : `%ProgramData%\StdMachine\projects\<nom>\` contenant `project.stdm.json`, `deployed.stdm.json` (copie exacte de ce qui a été déployé) et `history\` (une copie à chaque déploiement, 50 dernières).
- Sauvegarde automatique 2 s après la dernière modification ; indicateur « Enregistré il y a … » dans la barre du haut.
- JSON indenté, propriétés dans un ordre stable, `schemaVersion` ; une migration par version de schéma.
- Import et export d'un projet (`.stdm.json`) par la page Système.

### 5.2 Base SQLite

`%ProgramData%\StdMachine\data\stdmachine.db`, mode WAL.

| Table | Contenu | Rétention |
| --- | --- | --- |
| `users` | Identifiant, nom, niveau, empreinte du mot de passe (PBKDF2-SHA256, 100 000 itérations, sel), PIN facultatif, actif, dernière connexion | — |
| `levels` | N°, nom | — |
| `permissions` | Code du droit, niveau minimal | — |
| `orders` | OF (§ 16 de `02-specifications-fonctionnelles.md`) | 2 ans |
| `parts` | Identifiant, OF, création, sortie, état final, verdict | 2 ans |
| `traces` | Une ligne par opération sur une pièce (données de pièce en JSON) | 2 ans |
| `curves` | Courbes de presse (binaire compressé), liées à une trace | 6 mois |
| `alarms` | Historique : objet, code, sous-code, gravité, apparition, disparition, acquittement, utilisateur, contexte | 1 an |
| `audit` | Qui, quand, quoi, valeurs avant/après | 2 ans |
| `outbox` | Messages pour l'usine : seq, sujet, contenu, créé, envoyé | 30 jours ou 1 Go |
| `stops` | Arrêts : début, fin, état, cause, état déclaré, utilisateur | 2 ans |
| `forces` | Forçages : type, cible, motif, posé par, à, échéance, levé par, à | 2 ans |

Une purge tourne chaque nuit selon la rétention.

## 6. Services de l'application

| Service | Rôle |
| --- | --- |
| `IPlcClient` | Accès PLC (simulé ou ADS) — `06-tables-et-interface-hmi-plc.md` § 7 |
| `SnapshotStore` | Reçoit les instantanés, les publie au fil de l'interface à 10 Hz au plus (le plus récent seulement), expose des propriétés observables par machine, station, objet, mover |
| `ProjectService` | Projet ouvert, modifications par commandes annulables, sauvegarde automatique, compilation en continu |
| `UndoService` | Pile annuler/refaire par projet (100 niveaux) ; chaque modification est une commande `IUndoableEdit` |
| `CatalogService` | Catalogue chargé, recherche de types, d'actions, d'erreurs |
| `ValidationService` | Résultat de la dernière validation, filtré par page et par élément |
| `DeployService` | Déroulé du déploiement (§ 7.2 de `02-specifications-fonctionnelles.md`) |
| `AlarmService` | Flux d'alarmes du PLC → historique, textes (catalogue + fichiers XML de sous-alarmes `SubAlarms_*.xml`), alarmes actives, liens vers l'objet en cause |
| `TraceService` | Flux de traces → base, courbes, recherche de pièces |
| `OrderService` | OF via `IOrderSource` (`LocalOrderSource` en base) ; lancement vers le PLC |
| `SessionService` | Utilisateur connecté, niveau, déconnexion automatique |
| `PermissionService` | `Can(permission)`, niveau requis, raison du refus |
| `AuditService` | Écrit le journal |
| `NavigationService` | Pages, paramètres de navigation (station, objet, carte), historique « Retour » |
| `DialogService`, `ToastService` | Dialogues tactiles, notifications |
| `KeypadService` | Pavé numérique et clavier AZERTY en surimpression |
| `DragDropService` | Glisser-déposer tactile (§ 8) |
| `BlinkClock` | Horloge unique de clignotement (période = paramètre de la balise, 800 ms) pour que tout clignote en phase |
| `PlantGateway` | Interface usine : construit les messages depuis les services (instantané, alarmes, traces, OF, qualité, journal), les écrit dans l'outbox, les envoie en MQTT, republie les états conservés ; reçoit les OF de l'usine |
| `StopService` | Détecte les arrêts et leur cause ; propose la qualification par un état déclaré |
| `ForceService` | Forçages : pose, levée, échéances et rappels, impact d'une mise hors production (fonctions encore couvertes), enchaînement de la remise en production, journal ; forçages d'essai via `ISimulatorControl` |
| `CommunicationService` | Réglages des canaux (`communications.json`, secrets chiffrés par DPAPI), activation, tests, état en direct de chaque canal |
| `QualityService` | État de la surveillance des rebuts (instantané), libération et remise à zéro (droits, motif, journal), historique des réactions |
| `ScreenshotService` | Rendu d'une page hors écran en PNG (§ 10) |

## 7. Conventions MVVM

- Une vue = un `UserControl` + une vue-modèle ; le code-behind ne contient que de la logique purement visuelle (adorners, animations déclenchées). Pas de logique métier dans les vues.
- Les vues-modèles de page sont créées par l'injection de dépendances ; elles reçoivent leurs paramètres de navigation par `INavigationAware.OnNavigatedTo(args)`.
- Les commandes protégées sont des `GuardedCommand(permission, execute, canExecute)` : la commande vérifie le droit, journalise, et expose `RequiredLevel` pour l'affichage du cadenas.
- Les données en direct ne sont jamais copiées dans les vues-modèles par des minuteries : elles viennent du `SnapshotStore` (propriétés observables mises à jour à 10 Hz).
- Les animations sont des `Storyboard` WPF déclenchés par des états visuels (`VisualStateManager`) ou des `DataTrigger`, jamais par des minuteries dans les vues-modèles.
- Tous les textes affichés sont dans `Resources/Strings.resx` (français) ; les noms de code restent en anglais.
- Toutes les couleurs, tailles, rayons, durées viennent du dictionnaire de thème (`Theme/Tokens.xaml`) ; aucune valeur en dur dans les vues.

## 8. Infrastructure tactile

### 8.1 Réglages globaux

- `AppContext.SetSwitch("Switch.System.Windows.Input.Stylus.EnablePointerSupport", true)` au démarrage (pile WM_POINTER, toucher plus fiable).
- `Stylus.IsPressAndHoldEnabled = false` et `Stylus.IsFlicksEnabled = false` sur la fenêtre (pas de clic droit par appui long ni de gestes Windows).
- `ScrollViewer` : `PanningMode` adapté (vertical, horizontal ou les deux), `ManipulationBoundaryFeedback` intercepté (pas de rebond de la fenêtre).
- Fenêtre en plein écran sans bordure (mode kiosque réglable), curseur masqué en mode tactile (réglable).

### 8.2 Gestes

| Geste | Usage |
| --- | --- |
| Toucher | Activer, sélectionner |
| Appui long (500 ms) | Menu contextuel de l'élément (renommer, dupliquer, supprimer, déplacer vers…) avec retour visuel de remplissage circulaire pendant l'appui |
| Glisser depuis une poignée | Glisser-déposer |
| Glisser ailleurs | Défilement |
| Pincer (éditeur de process, plan planaire, vue de dessus) | Zoom de 50 % à 200 % |
| Maintenir (bouton de jog, bouton de confirmation dangereuse) | Action tant que tenu / confirmation après 1 s |

### 8.3 Service de glisser-déposer

- API par propriétés attachées : `dd:Drag.Source="{Binding Payload}"`, `dd:Drag.Handle="True"` (élément qui démarre le glisser), `dd:Drop.Target="{Binding DropHandler}"` (implémente `IDropTarget` : `CanAccept(payload, point) → DropPreview`, `Drop(payload, preview)`).
- Démarrage : depuis une poignée, dès 8 px de déplacement ; depuis le reste d'un élément glissable, après appui long de 300 ms sans bouger (pour ne pas voler le défilement).
- Le même code traite souris, stylet et toucher (événements pointeur unifiés), avec capture du pointeur.
- Fantôme : copie visuelle de l'élément (opacité 90 %, ombre portée, légère rotation de 2°, décalé de 12 px sous le doigt pour rester visible) dans une couche d'adorner au-dessus de toute la fenêtre.
- Cible : la zone survolée affiche son aperçu (`DropPreview`) — trait bleu d'insertion avant/après une carte, zone vide d'une branche en surbrillance, emplacement de la vue de dessus en surbrillance ; une cible refusée affiche un fond rouge pâle et la raison en infobulle sous le doigt.
- Défilement automatique quand le doigt est à moins de 48 px du bord d'une zone défilante (vitesse proportionnelle à la proximité).
- Annulation : relâcher hors cible, ou toucher avec un second doigt ; le fantôme revient à sa place en 200 ms.
- Chaque dépôt est une commande annulable.
- Page de démonstration du service (`Système › Essai tactile`), qui sert aussi aux tests de réception sur l'écran réel.

### 8.4 Saisie

- Champ numérique : toucher → pavé numérique en surimpression, ancré près du champ, avec unité, bornes, valeur actuelle, touches −/+ du pas, effacement, validation ; valeur hors bornes refusée avec message.
- Champ texte : clavier AZERTY en surimpression, accents, majuscules.
- Listes : liste déroulante tactile (éléments de 56 px, recherche au-delà de 12 éléments).

## 9. Simulateur (StdMachine.Simulation)

- `SimulatedPlcClient : IPlcClient, ISimulatorControl`.
- Contient : `SimPlc` (cycle de 10 ms × échelle de temps sur un fil dédié), les modèles d'objets (un par type, `03-catalogue-objets.md`), l'interpréteur, les séquenceurs de station et général, le gestionnaire de movers (plan 2D), le chargeur (mêmes vérifications que le PLC), le gestionnaire d'alarmes, le tampon de traces.
- Fidélité : il applique `05-plc-sequenceurs.md` à la lettre ; tout écart est un défaut.
- Il démarre avec le projet `samples/MX-4.stdm.json` déployé, machine Stopped, mode Production, 6 movers sur des tampons.
- Panneau « Simulateur » (page Système et bouton dans la barre du haut en mode développement) : échelle de temps, injection de défauts (erreur d'objet, Abort de station, circuit de sécurité ouvert), taux d'échec par type, forçage d'entrées.

### 9.1 Maquette et mode démonstration

- Avant le simulateur, la HMI tourne sur `DemoPlcClient : IPlcClient` (projet `StdMachine.Simulation`) qui **rejoue un scénario scripté** : une suite d'instantanés `PlcSnapshot` construits à la main pour la machine MX-4 (états machine et stations, positions des movers, PC des contextes et cartes en cours, alarmes, qualité, forçages), interpolés dans le temps et rejoués en boucle. Les commandes modifient l'état de démonstration de façon plausible (Démarrer lance le scénario, Abort le fige en Aborted, Acquitter puis Réarmer le remettent au début).
- Le projet de démonstration est construit en code (`DemoProject`) avec le modèle de process de § 3.2 ; l'éditeur le modifie en mémoire (annuler/refaire actifs, pas d'enregistrement).
- Les vues et vues-modèles de la maquette sont définitives : les phases suivantes remplacent `DemoPlcClient` par `SimulatedPlcClient` et `DemoProject` par le projet chargé, sans réécrire les pages.
- Tant que `DemoPlcClient` est actif, la barre du haut affiche la puce « Données de démonstration ». Ensuite, il ne sert plus qu'aux tests d'interface (scénarios figés reproductibles).

## 10. Vérification visuelle et scénarios

L'application accepte des options de ligne de commande, utilisées pour vérifier le rendu sans écran :

```
StdMachine.Hmi.exe --screenshot <page> <fichier.png> [--size 1920x1080] [--scenario <nom>] [--at <secondes>]
StdMachine.Hmi.exe --screenshot-all <dossier> [--scenario <nom>]
```

- `<page>` : Machine, Station:<n>, Manual:<n>, Alarms, Quality, Forces, Parts, Orders, MachineConfig, StationConfig:<n>, ProcessEditor:<station>:<process>, ProcessEditor:General, Deploy, Users, Stats, System, Communications, DragDropTest.
- Scénario de la maquette : `demo` (instantanés du scénario scripté à 0, 30, 60 et 90 s).
- Scénarios (simulateur) : `idle` (machine Stopped), `running` (production depuis 30 s), `station-aborted` (Pressage 01 Aborted, production continue), `step-mode` (Pressage 02 en pas à pas, arrêtée dans un bloc parallèle), `manual` (Maintenance, mode manuel Pressage 01), `validation-errors` (projet avec erreurs V001 et V004), `quality-hold` (taux de rebut de la presse de Pressage 02 forcé : Pressage 02 en arrêt qualité par imputation, production sur Pressage 01), `forced` (maintenance corrective déclarée, Pressage 01 hors production en mode Maintenance, mover 3 hors service, la machine produit avec Pressage 02).
- Le rendu se fait hors écran (`RenderTargetBitmap`) après stabilisation des animations (elles sont figées à leur état médian pour la capture).
- `--screenshot-all` produit toutes les pages pour tous les scénarios : c'est la base de comparaison avec la maquette et de revue du design.

## 11. Démarrage

1. Lire `appsettings.json` (mode PLC : `Simulation` ou `Ads`, AMS Net Id, port 851, chemins, délai de déconnexion, mode kiosque).
2. Ouvrir la base (création et migrations au premier lancement, compte admin).
3. Charger le catalogue et les fichiers de sous-alarmes.
4. Ouvrir le dernier projet (sinon le projet d'exemple).
5. Créer le client PLC, se connecter ; écran de démarrage avec l'état de chaque étape.
6. Afficher la page Machine.

## 12. Tests obligatoires

| Projet | Tests |
| --- | --- |
| Core.Tests | Catalogue cohérent ; compilation de chaque carte et de chaque bloc (listing attendu) ; dépliage des fonctions du robot ; allocation des variables cachées, conditions, textes ; chaque règle de validation (un cas qui la déclenche, un cas qui ne la déclenche pas) ; droits (niveaux, refus, Abort toujours permis) ; override effectif et timeout effectif (exemples de `02-specifications-fonctionnelles.md` § 13) ; carte de compilation (instruction ↔ carte) |
| Simulation.Tests | Toutes les transitions PackML (et les refus) ; chaque instruction de l'interpréteur ; Fork/Join à 2, 3, 4 branches ; Hold et Stop qui laissent finir l'opération ; Abort pendant une opération ; politiques d'échec ; pas à pas aux trois granularités, y compris en parallèle ; canal manuel (états refusés, verrouillage, garde du jog) ; routage (choix de station, file, tampon, rebut, mover à vider) ; station Aborted dans une fonction de deux stations ; fin d'OF ; persistance des movers après Stop/Start ; machine d'exemple : 100 pièces en simulation accélérée, toutes sorties ou rebutées |
| Core.Tests (bibliothèque) | Une fonction de bibliothèque utilisée par deux stations se déplie sur chaque module avec ses propres objets ; modification propagée aux deux (validation relancée) ; V030, V031 ; suppression refusée si utilisée ; publication d'une fonction de station (conversion en rôles, refus d'une variable de station) ; fichier projet : aller-retour sans perte de la bibliothèque et lecture d'un projet sans bibliothèque |
| Core.Tests (sorties de blocs) | Fonction personnalisée à deux sorties dépliée deux fois avec des cibles différentes (aucune instruction en plus) ; sous-process à sorties : emplacement alloué, copie après `Call`, comptage dans les 64 ; V028 (deux branches, opération et fond), V121 (chemin sans définition), V029 ; V014 sur type incompatible ; signature d'opération avec sorties et V017 |
| Simulation.Tests (sorties de blocs) | « Rechercher pièce Asyril » rend position et nombre, rangés dans deux variables de process puis lus par « Prendre pièce » ; sous-process appelé deux fois de suite avec des cibles différentes ; opération `Done` copie ses sorties vers une donnée de pièce et une variable de mover ; opération `Failed` ou avortée ne copie rien ; sortie non rangée ignorée ; position copiée avec son décalage |
| Simulation.Tests (variables de mover) | Écriture et lecture par une opération puis par le process général (nombre, booléen, position avec décalage) ; deux movers gardent chacun leur valeur ; lecture dans la préparation avant l'arrivée ; remise à zéro `OnEmpty` à la sortie, au rebut, au retrait confirmé et à la pièce « retirée » (pas tant qu'une autre pièce du mover reste) ; `OnDeploy` ; `Never` survit au Stop, au Reset, à l'Abort et à un redéploiement qui ajoute, retire ou réordonne des variables (identifiant) ; type changé = valeur initiale ; étape Réinitialiser (une variable, toutes) ; commande de la HMI (acceptée en Maintenance Stopped, refusée en Execute) ; refus V026 dans le cycle de fond |
| Simulation.Tests (qualité) | Tous les tests de `10-surveillance-des-rebuts.md` § 12 |
| Plant.Tests | Tests de première version de `11-interface-usine.md` § 8 (canal Journal de messages, schémas, `seq`, réglages) |
| Simulation.Tests (forçages) | Tous les tests de `12-forcages-et-etats-declares.md` § 9 |
| Persistence.Tests | Aller-retour JSON du projet d'exemple sans perte ; migrations de schéma ; dépôts SQLite |
