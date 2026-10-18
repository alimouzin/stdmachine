# Glossaire

Ce glossaire fixe le sens des mots utilisés dans tous les documents, dans le code (noms en anglais entre parenthèses) et dans l'interface. En cas de doute sur un mot, ce document fait foi. Un mot absent d'ici ne doit pas être inventé dans l'interface : l'ajouter d'abord ici.

## Structure de la machine

| Terme | Code | Définition |
| --- | --- | --- |
| Machine | `Machine` | Ensemble de la ligne : le système planaire, toutes les stations et les objets de niveau machine (balise machine, sécurités, système planaire). Elle a un séquenceur général, un override et un process général. |
| Système planaire | `PlanarSystem` | Le Beckhoff Planar (ou équivalent) au centre de la machine : des tuiles sur lesquelles des movers lévitent. C'est un objet de niveau machine. |
| Mover | `Mover` | Chariot lévitant du système planaire. Il porte de 1 à 4 pièces (une par emplacement) et se déplace entre les stations. Chaque mover exécute sa propre instance du process général. |
| Emplacement | `Slot` | Place numérotée (1 à 4) d'une pièce sur un mover. |
| Pièce | `Part` | Composant fabriqué. Elle a un identifiant, un état, des données de pièce et un historique d'opérations. |
| Station | `Station` | Poste de travail fixe autour du système planaire. Elle a un séquenceur de station, des objets, des positions nommées, une balise, un override et des opérations. Elle appartient à une ou plusieurs fonctions. |
| Fonction | `Group` | Capacité de la machine utilisée à un moment du process général (par exemple « Pressage pignon »), avec un rôle, une priorité, des opérations et les stations qui savent la réaliser. Une station appartient à une ou plusieurs fonctions : une station de pressage peut réaliser « Pressage pignon » au début du cycle et « Sertissage » à la fin. Le process général ne connaît que des fonctions. Dans le code (HMI et PLC), on dit `Group` (`GroupConfig`, `GroupId`) pour ne pas confondre avec les fonctions de module. |
| Rôle de fonction | `GroupRole` | Entrée (ses opérations chargent les pièces sur les movers et les créent), Travail, Sortie (déchargent les pièces bonnes et les clôturent), Rebut (déchargent les pièces mauvaises). Le rôle est porté par la fonction, pas par la station : une station de chargement et déchargement appartient à une fonction Entrée et à une fonction Sortie. |
| Priorité de fonction | `GroupPriority` | Ordre de service des movers en file devant une station partagée par plusieurs fonctions : la fonction de plus haute priorité passe d'abord. Par défaut, la fonction la plus avancée dans le process général est la plus prioritaire, pour vider la machine plutôt que la remplir. |
| Position de travail | `WorkPosition` | Position du mover dans une station pendant une opération. |
| File d'attente | `Queue` | Positions où des movers attendent devant une station occupée (0 à 4 par station). |
| Position tampon | `Buffer` | Position du plan où un mover attend quand aucune station de la fonction visée ne peut l'accueillir. |
| Objet | `Object` (code PLC : `FB_xxx`) | Élément physique ou logique déclaré dans une station ou au niveau machine : vérin, axe, presse, vision, compteur… Il a des paramètres, des signaux, un état, des actions et des alarmes. On ne dit pas « dispositif » ni « device » dans l'interface. |
| Type d'objet | `ObjectType` | Modèle d'objet défini dans le catalogue (par exemple « Vérin »). Une station contient des instances de types. |
| Module | `Module` (PLC : `FB_Group`) | Objet qui contient d'autres objets (ses enfants) et fournit des fonctions de module. Exemple : le robot cartésien XYZC contient quatre axes et un outil. |
| Objet parent, objet enfant | `Parent`, `Child` | Relation de contenance : machine ⊃ station ⊃ module ⊃ objet. |
| Rôle d'enfant | `ChildRole` | Place nommée qu'un enfant occupe dans un module (axe X, axe Z, outil…). Les fonctions de module s'écrivent sur des rôles, pas sur des objets précis. |
| Catalogue | `Catalog` | Fichier qui décrit tous les types d'objets et de modules : paramètres, actions, résultats, état affiché, alarmes. Source unique de vérité pour la HMI et le PLC. |
| Pool | `Pool` | Tableau statique d'instances d'un type d'objet dans le PLC. La configuration attribue un élément du pool à chaque objet déclaré. |
| Référence de signal | `SignalRef` | Adresse d'une entrée ou d'une sortie dans l'image E/S générique du PLC (type, index, inversion). |
| Image E/S | `IoImage` | Tableaux génériques d'entrées et de sorties du PLC, reliés une fois pour toutes aux bornes dans TwinCAT. Les objets y lisent et écrivent par référence de signal. |
| Raccordement | `Wiring` | Association des signaux d'un objet aux références de signal ou à un élément matériel (axe NC, esclave EtherCAT). |

## Actions, opérations et process

| Terme | Code | Définition |
| --- | --- | --- |
| Action de base | `Action` | Action disponible sur un objet (Move travail, Aspirer, Lire…). Niveau 2. Toute action respecte le contrat d'action. |
| Contrat d'action | `ActionContract` | Règles communes à toutes les actions : démarrage accepté seulement si l'objet est Prêt, statut En cours / Terminée / En erreur, timeout, arrêt sur Abort vers un état sûr, résultats. |
| Action de mouvement | `IsMotion` | Action qui déplace quelque chose (axe, vérin, mover, robot externe). Seules ces actions sont ralenties par l'override. |
| Résultat | `Result` | Valeur renvoyée par une action : nom, valeur, unité, jugement. |
| Jugement | `Judgement` | Qualification d'un résultat : aucun, bon, mauvais, trop haut, trop bas. |
| Fonction de module | `ModuleFunction` | Suite d'actions de base fournie par un module (Prendre pièce, Poser pièce, Aller à). Niveau 3. Elle s'écrit sur les rôles d'enfant et se déplie dans les tables à la compilation. |
| Fonction personnalisée | `CustomFunction` | Copie modifiée d'une fonction de module, créée par l'ingénieur process. La fonction d'origine reste disponible. |
| Opération | `Operation` | Ce qu'une fonction sait faire sur un mover (par exemple « Presser pignon »), appelé par le process général. Niveau 4. Chaque station membre de la fonction l'implémente par un de ses process ; un même process de station peut implémenter des opérations de plusieurs fonctions. |
| Process de station | `StationProcess` | Une suite d'étapes définie dans l'éditeur au niveau station. Types : opération, cycle de fond, initialisation, arrêt, vidage, sous-process. |
| Cycle de fond | `Background` | Process de station qui tourne en boucle tant que la station est en Execute, indépendamment des movers (par exemple préparer la pièce suivante). Optionnel. |
| Process général | `GeneralProcess` | Enchaînement des opérations qu'un mover doit subir, de l'entrée à la sortie. Niveau 5. Chaque mover en exécute sa propre instance. |
| Sous-process | `SubProcess` | Process appelé depuis un autre process du même niveau, puis retour. |
| Étape | `Step` | Une carte de l'éditeur : une action, une fonction de module, une opération (niveau machine) ou une instruction de logique. |
| Bloc | `Block` | Structure de logique qui contient des branches d'étapes : En parallèle, Si / Sinon, Répéter. |
| Branche | `Branch` | Suite d'étapes à l'intérieur d'un bloc. |
| Couloir | `Lane` | Colonne de l'éditeur en mode d'affichage « Couloirs » (option). Niveau station : un couloir par objet de premier niveau (module ou objet seul) plus un couloir Logique. Niveau machine : un couloir par fonction. Le mode par défaut est le **Flux** : une seule colonne, sans couloirs ; les colonnes n'existent que dans les blocs En parallèle et Si / Sinon. Le couloir est une mise en page, pas une notion du modèle. |
| Calibration | `Calibration` | Procédure de réglage lancée en Maintenance, qui mesure ou apprend des valeurs (positions, décalages, facteurs) et les enregistre durablement. Process de station, ou process machine qui enchaîne des calibrations de station. |
| Variable persistante | `RetainedVar` | Variable de station dont la valeur survit au Stop, au redéploiement et à la coupure ; seule une calibration l'écrit. |
| Bibliothèque de fonctions | `FunctionLibrary` | Ensemble des fonctions personnalisées de la machine, rangées par type de module. Une fonction de bibliothèque se décrit avec les rôles du module (Axe X, Outil…), pas avec des objets précis, donc toute station qui a un module de ce type l'utilise sans copie. |
| Préparation | `Prepare` | Partie d'une opération qui précède l'étape « Mover requis » : elle tourne dès que la position de travail est réservée à un mover, pendant que celui-ci arrive, et ne touche ni la pièce ni l'emplacement (par exemple prendre la pièce dans l'alimenteur). |
| Mover requis | `WaitMover` | Étape de séparation d'une opération : ce qui est avant est la préparation, ce qui est après exige le mover en position de travail. |
| Fin des pièces en cours | `Drain` | Mode d'arrêt de la machine : plus aucune pièce n'est chargée, les pièces déjà engagées terminent leur process, puis la machine s'arrête (Stop automatique). Bouton « Finir les pièces ». À ne pas confondre avec le process de **vidage** (état Clearing) ni avec un mover « à vider » : le mot « vider » n'est pas employé pour ce mode. |
| Flux | `Flow` | Mode d'affichage par défaut de l'éditeur de process : cartes empilées dans une colonne, une pastille sur chaque carte rappelle l'objet concerné. |
| Pastille d'objet | `ObjectChip` | Icône et nom de l'objet de premier niveau, portés par chaque carte en mode Flux. |
| Variable de station | `StationVar` | Mémoire d'une station partagée par tous ses process (par exemple la position trouvée par la vision, utilisée ensuite par la prise). |
| Variable de process | `ProcessVar` | Mémoire nommée propre à un process, invisible des autres. Créée à la demande depuis la sortie d'une étape ; peut être rendue partagée (elle devient alors une variable de station). |
| Sortie | `Output` | Valeur rendue par une étape : résultat d'une action rangé dans une variable nommée, ou sortie déclarée d'un bloc composé (fonction personnalisée, sous-process, opération). Une sortie sans nom se désigne par son rang (« Sortie 2 »). |
| Bloc composé | `CompositeBlock` | Étape qui en contient d'autres : fonction personnalisée, appel de sous-process, opération. À ne pas confondre avec les blocs de structure (Si / Sinon, En parallèle, Répéter). |
| Variable de mover | `MoverVar` | Information propre à un mover (nombre, booléen ou position), déclarée dans la configuration machine (16 au plus) ; chaque mover a son jeu de valeurs. Elle suit le mover, pas la pièce. Règle de remise à zéro : jamais, au déploiement, ou quand le mover est vide. |
| Donnée de pièce | `PartData` | Mémoire attachée à une pièce, qui la suit de station en station (résultats, verdict, compteurs de retouche). |
| Verdict | `Verdict` | Donnée de pièce réservée : non défini, bon, mauvais. Elle décide de la sortie de la pièce. |
| Condition | `Condition` | Jusqu'à 4 comparaisons combinées par « toutes vraies » ou « au moins une vraie ». Utilisée par Si / Sinon, Attendre condition et les verrouillages. |
| Verrouillage | `Interlock` | Condition qui doit être vraie pour qu'une action d'un objet démarre et continue (par exemple : pas de mouvement X/Y si Z est sous la hauteur de sécurité). Actif dans tous les modes. |
| Politique d'échec | `FailPolicy` | Ce que fait une étape quand son action échoue : arrêter la station, refaire N fois, marquer la pièce mauvaise et continuer, sauter à une étape. |
| Compilation | `Compile` | Transformation, dans la HMI, des process (arbres de blocs) en tables d'instructions. |
| Table | `Table` | Forme compilée d'un process : une suite d'instructions plates exécutée par un séquenceur PLC. |
| Instruction | `Instruction` | Ligne d'une table : action, attente, saut, branchement, appel, marqueur… |
| Contexte d'exécution | `Context` | « Fil » qui exécute des instructions dans un séquenceur. Un bloc En parallèle utilise un contexte par branche. |
| Séquenceur général | `GeneralSequencer` | Partie du PLC qui gère l'état de la machine, les commandes, les movers et l'exécution du process général. Un seul. |
| Séquenceur de station | `StationSequencer` | Partie du PLC qui gère l'état d'une station et exécute ses tables. Un par station. |
| Routage | `Routing` | Choix, par le séquenceur général, de la station d'une fonction qui va traiter un mover. |
| Valider | `Validate` | Vérifier une configuration ou un process et lister erreurs et avertissements. |
| Simuler | `Simulate` | Faire tourner la HMI contre le simulateur de PLC, sans machine. |
| Déployer | `Deploy` | Compiler, envoyer la configuration et les tables au PLC et les activer. Seulement machine à l'état Stopped. |
| Projet machine | `MachineProject` | Fichier qui contient toute la définition d'une machine : stations, objets, paramètres, positions, process. |

## États, commandes et modes

| Terme | Code | Définition |
| --- | --- | --- |
| PackML réduit | `PackMlState` | Modèle d'états appliqué par chaque séquenceur : 8 états, 6 commandes, 2 modes. |
| États | `Stopped`, `Resetting`, `Idle`, `Execute`, `Held`, `Stopping`, `Aborted`, `Clearing` | Noms anglais conservés dans l'interface (vocabulaire PackML), avec une description française au survol de l'aide. |
| Commandes | `Reset`, `Start`, `Stop`, `Hold`, `Abort`, `Clear` | Libellés d'interface : Réarmer, Démarrer, Arrêter, Pause, Arrêt d'urgence logiciel (Abort), Acquitter. Le libellé « Démarrer » sert aussi à reprendre depuis Held. |
| Abort | `Abort` | La commande qui arrête immédiatement les mouvements et met les objets en état sûr. |
| Aborted | `Aborted` | L'état qui en résulte (commande ou défaut). Il est local à la station quand la fonction peut continuer sans elle. |
| Mode Production | `Production` | Mode d'exécution automatique normal. |
| Mode Maintenance | `Maintenance` | Mode de réglage et de test. Le mode manuel et le mode pas à pas n'existent que dans ce mode. |
| Mode manuel | `Manual` | Exécution d'une action isolée d'un objet ou d'une fonction de module, station à l'état Stopped, en mode Maintenance. |
| Mode pas à pas | `StepMode` | Exécution d'une opération en s'arrêtant avant chaque opération, chaque étape ou chaque action de base, au choix. Pas de retour en arrière ni de saut ; seul Abort permet d'abandonner. |
| Réintégration | `Reintegrate` | Retour d'une station Aborted dans ses fonctions pendant que la machine produit : Acquitter puis Réarmer puis Démarrer la station seule. |
| Balise | `Beacon` | Colonne lumineuse avec buzzer, pilotée par l'état du séquenceur auquel elle est associée. Une balise machine, une par station. Ne pas confondre avec un signal. |
| État d'objet | `ObjectState` | Non initialisé, Prêt, En cours, En erreur, Désactivé. |
| Simulation d'objet | `Simulated` | Un objet en simulation n'accède pas au matériel et simule son comportement. Sert à tester sans matériel. |

## Position et mouvement

| Terme | Code | Définition |
| --- | --- | --- |
| Position | `Position` | Toujours une structure X, Y, Z, C (rotation Rz, en degrés) et un frame. |
| Frame | `Frame` | Repère dans lequel une position est exprimée : 0 = machine (plan planaire) ou repère propre des axes de la station. Les autres n° de frame ne servent qu'aux **robots Stäubli** : ils désignent un repère défini dans le robot, qui fait lui-même toutes les transformations. La HMI et le PLC ne transforment jamais une position. |
| Position nommée | `NamedPosition` | Position enregistrée et nommée dans une station (repos, prise, dépose). Paramètre de la station, jamais de l'axe. |
| Position actuelle | `ActualPosition` | Valeur lue sur un axe, un robot ou un mover. |
| Apprendre | `Teach` | Copier la position actuelle dans une position nommée. |
| Composante | `Component` | X, Y, Z ou C. Un axe linéaire pilote une composante X, Y ou Z ; un axe rotatif pilote C. |
| Position de homing | `HomePosition` | La seule position portée par un axe : sa position de référencement. |
| Vmax | `VMax` | Vitesse maximale d'un axe (mm/s ou °/s), paramètre global de l'axe. |
| Vitesse | `Speed` | Toujours en % de la Vmax. Standard : lente (n° 1) et rapide (n° 2). Personnalisées : n° 3 à 8, avec un nom. |
| Override | `Override` | Facteur de vitesse de 0 à 100 % qui ne fait que ralentir. Il existe aux niveaux machine, station, module et axe. Il ne s'applique qu'aux actions de mouvement. |
| Override effectif | `EffectiveOverride` | Produit des overrides de tous les niveaux qui contiennent l'objet, en poupées russes. Exemple : 80 % × 50 % × 100 % = 40 %. |
| Override manuel | `ManualOverride` | Override supplémentaire d'un axe, appliqué en plus quand l'action est commandée en mode manuel. |
| Timeout nominal | `NominalTimeout` | Timeout d'une action à override 100 %. |
| Timeout effectif | `EffectiveTimeout` | Pour une action de mouvement : timeout nominal divisé par l'override effectif. À 0 % l'axe est arrêté et le timeout suspendu. |
| Jog | `Jog` | Déplacement manuel tant que le bouton est maintenu. |

## Exploitation et HMI

| Terme | Code | Définition |
| --- | --- | --- |
| HMI | `Hmi` | L'application WPF. Elle contient le studio et les pages d'exploitation. |
| Studio | `Studio` | Partie ingénierie de la HMI : configuration machine et stations, éditeur de process, validation, déploiement. |
| Page d'exploitation | `Operations` | Page utilisée pendant la production : vue machine, station, manuel, pas à pas, alarmes, pièces, OF. |
| Ingénieur process | — | Personne qui définit les stations, les opérations et le process général dans le studio. |
| Régleur | — | Personne qui règle la machine : positions, mode manuel, pas à pas. |
| Opérateur | — | Personne qui fait tourner la machine : commandes, OF, acquittement des alarmes. |
| Niveau d'utilisateur | `UserLevel` | Niveau de droits, numéroté et nommé, définissable. Par défaut : 0 Opérateur, 1 Régleur, 2 Ingénieur process, 3 Administrateur. |
| Droit | `Permission` | Type d'action protégé (démarrer la machine, déployer, apprendre une position…). L'administrateur règle le niveau minimal de chaque droit. |
| OF (ordre de fabrication) | `ProductionOrder` | Commande de production : numéro, référence, quantité, opérations à utiliser. Local pour le moment, derrière une interface de source d'OF. |
| Recette de presse | `PressRecipe` | Programme de pressage exécuté par la presse, désigné par un numéro. Ne jamais dire « recette » seul. |
| Recette de vision | `VisionRecipe` | Produit à chercher par la vision Asyril, désigné par un numéro. |
| Alarme | `Alarm` | Événement d'erreur ou d'avertissement : objet, code, sous-code, gravité, horodatage. Le texte vient du catalogue et des fichiers de sous-alarmes. |
| Sous-alarme | `SubCode` | Numéro secondaire d'une alarme (par exemple le code d'erreur du drive), traduit par les fichiers XML de sous-alarmes. |
| Trace | `TraceRecord` | Enregistrement d'une opération réalisée sur une pièce : station, opération, début, fin, résultats, verdict. |
| Rebut constaté | `DetectedReject` | Pièce dont le verdict passe à mauvais pendant une opération : compté pour la station, la fonction et le mover de cette opération. |
| Rebut imputé | `ImputedReject` | Pièce jugée mauvaise par une fonction de contrôle, comptée aussi contre les stations amont qui l'ont travaillée. |
| Rebut sur défaut | `FaultReject` | Pièce mauvaise parce que sa station est passée Aborted ; exclue des successifs et des taux. |
| Rebuts successifs | `ConsecutiveRejects` | Nombre de résultats mauvais d'affilée dans une portée ; un bon le remet à zéro. |
| Taux de rebut | `RejectRate` | Proportion de mauvais dans la fenêtre glissante d'une portée. |
| Fenêtre glissante | `SlidingWindow` | Les N derniers résultats d'une portée (en nombre de résultats, jamais en temps). |
| Arrêt qualité | `QualityHold` | Réaction qui met une station en Held et la retire du routage jusqu'à une libération qualité. |
| Mover hors service | `OutOfService` | Mover retiré du process (réaction qualité ou forçage) ; il attend vide en parcage. |
| Forçage | `Force` | Action volontaire et tracée qui agit autour du séquenceur sans le forcer : état déclaré, station hors production, mover hors service, essai en simulation. |
| État déclaré | `DeclaredState` | Situation de la machine déclarée par une personne (arrêt planifié, attente amont, maintenance, essais, nettoyage…), publiée à l'usine ; certains états bloquent le démarrage. |
| Démarrage bloqué | `StartBlocked` | La commande Démarrer de la machine est refusée tant qu'un état déclaré bloquant est actif ; Arrêter, Pause et Abort restent permis. |
| Station hors production | `OutOfProduction` | Station retirée volontairement du routage pour une intervention ; elle peut passer seule en Maintenance pendant que la machine produit. |
| Forçage d'essai | `TestForce` | Événement provoqué dans le simulateur (état de station, défaut, résultat, entrée) pour tester un comportement. N'existe pas sur une vraie machine. |
| Canal de communication | `Channel` | Liaison réglable et activable dans la page Communications : PLC, journal de messages, usine MQTT, usine OPC UA, OF entrants, appareils réseau. |
| Arrêt du chargement | `StopLoading` | Plus aucune nouvelle pièce n'entre ; les pièces en cours terminent leur process. |
| Libération qualité | `QualityRelease` | Levée d'une réaction qualité par une personne habilitée, avec un motif. |
| Surveillance renforcée | `Probation` | Après une libération, les 10 résultats suivants déclenchent l'action dès 2 rebuts successifs. |
| Budget de rebuts | `RejectBudget` | Nombre ou pourcentage maximal de pièces mauvaises admis pour un OF. |
| Interface usine | `Plant` | Remontée des informations de la machine vers le système général de l'usine (OPC UA côté PLC, MQTT côté HMI) et réception des OF. |
| Passerelle usine | `PlantGateway` | Service de la HMI qui publie les événements en MQTT à travers l'outbox. |
| Outbox | `Outbox` | File d'envoi persistante : chaque message y est écrit avant d'être envoyé, pour ne rien perdre pendant une coupure. |
| PackTags | `PackTags` | Noms standard PackML des données d'état et de compteurs (état courant, mode, compteurs de pièces, temps cumulés, raison d'arrêt), utilisés pour l'OPC UA. |
| Raison d'arrêt | `StopReason` | Alarme première cause d'un arrêt, ou raison choisie par l'opérateur quand l'arrêt est qualifié. |
| Journal | `AuditLog` | Enregistrement de qui a fait quoi dans la HMI (commandes, modifications, déploiements). |

## Mots à ne pas utiliser

| À éviter | Dire plutôt |
| --- | --- |
| Dispositif, device (dans l'interface) | Objet |
| Recette (seul) | Recette de presse, recette de vision, ou OF selon le cas |
| Séquence (pour ce qu'on édite) | Process ; « séquence » désigne seulement le déroulement à l'exécution |
| Programme (pour un process) | Process ou opération ; « programme » est réservé aux programmes internes d'un appareil (visseuse, Keyence) |
| Tag | Signal ou variable |
| Poste | Station (sauf « poste opérateur » pour une station où un opérateur intervient) |
| Groupe (dans l'interface) | Fonction |
