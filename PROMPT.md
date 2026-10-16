Lis `CLAUDE.md`, puis tous les documents de `docs/` dans l'ordre de `docs/README.md`, en entier. Ouvre aussi `docs/ui-reference/Machine_Studio.dc.html` et lis son code : c'est la maquette de référence pour le design et pour l'éditeur de process.

## Objectif

Construire un **premier aperçu complet et fonctionnel** de la HMI de la machine standard, en WPF (.NET 10), contre un **simulateur fidèle du PLC**, avec la machine d'exemple MX-4 de `docs/01-definition-machine.md`. À la fin, je dois pouvoir : voir la machine produire en simulation sur le plan planaire, suivre l'exécution d'une opération carte par carte dans l'éditeur, provoquer un défaut de station et réintégrer la station, piloter un objet en manuel, faire du pas à pas, modifier un process par glisser-déposer au doigt, le valider et le déployer sur le simulateur.

Le code PLC (ST) et le client ADS ne font **pas** partie de ce travail (phase 9, seulement sur demande).

## Ce qui est déjà décidé

Toutes les décisions techniques et fonctionnelles sont dans les documents et dans `docs/09-journal-des-decisions.md`. Ne les rediscute pas. S'il manque une décision, prends la plus simple qui respecte les documents, inscris-la dans le journal et signale-la dans ton compte rendu. Ne me pose une question que si une décision est irréversible ou contredit un document.

## Exigence de qualité de l'interface

Le premier essai d'interface fait sans référence visuelle ne m'a pas convenu : écrans génériques, formulaires, boutons gris. Ici, chaque écran doit suivre `docs/08-hmi-design-ecrans.md` (jetons, composants, disposition, retours visuels) et l'éditeur de process doit ressembler à l'écran Process de la maquette, agrandi pour le toucher. Après chaque écran, produis les captures (`--screenshot-all`), **regarde-les**, compare-les au document et à la maquette, et corrige avant de passer à la suite. Dans ton compte rendu, mets les chemins des captures.

## Méthode

1. Commence par un **plan global** sans écrire de code : structure de la solution, liste des composants d'interface, ordre des phases ci-dessous avec ce que tu livres dans chacune, contenu du scénario de démonstration de la phase 1, risques techniques (en particulier le glisser-déposer tactile en WPF et le rendu du canevas de l'éditeur). **Attends mon accord.**
2. Ensuite, phase par phase. À la fin de chaque phase : build sans avertissement, tests verts, captures regardées, commit(s), compte rendu court (fait, reste, écarts, décisions). **Attends mon « go » avant la phase suivante**, sauf si je t'ai dit d'enchaîner.

## Phases

Le travail est découpé pour valider le **proof of concept** le plus tôt possible : après les fondations, une maquette navigable de toute la HMI, avec des données de démonstration, me permet de juger l'aspect, la navigation, le tactile et l'éditeur de process sur l'écran réel **avant** d'investir dans le domaine, le compilateur et le simulateur. Les vues et vues-modèles de la maquette sont les vraies : les phases suivantes ne les jettent pas, elles remplacent la source de données.

### Phase 0 — Fondations

- Si le dossier n'est pas un dépôt Git : `git init`, branche `main`, remote `git@github.com:alimouzin/stdmachine.git`, `.gitignore`, premier commit avec `docs/`, `CLAUDE.md`, `PROMPT.md`. Puis branche `develop`.
- Solution et projets de `docs/07` § 2, `Directory.Build.props` (nullable, avertissements), références entre projets.
- Polices : télécharger IBM Plex Sans (TTF statiques 400, 500, 600, dépôt GitHub d'IBM) et Material Symbols Outlined (TTF et fichier `.codepoints`, dépôt Google) dans `assets/fonts/` ; générer la table nom → point de code des icônes utilisées ; contrôle `Icon`.
- Thème : `Theme/Tokens.xaml` avec tous les jetons de `docs/08` § 2 ; styles par défaut.
- Composants de base de `docs/08` § 3 : Button (variantes), CommandButton, AbortButton, HoldButton, StateBadge, StatusPill, StatusDot, Tabs, Segmented, Toggle, OverrideControl, NumericKeypad, Keyboard, Toast, Drawer, Beacon, ProgressBar, ProgressRing, EmptyState, LockOverlay.
- `BlinkClock`, réglages tactiles globaux (`docs/07` § 8.1), `DragDropService` complet (`docs/07` § 8.3) et page **Système › Essai tactile** : une palette, une liste réordonnable, deux branches parallèles, une zone refusée, avec défilement automatique.
- Mode capture `--screenshot` / `--screenshot-all` (`docs/07` § 10).
- **Critères** : l'application démarre en plein écran 1920 × 1080 ; une page de démonstration des composants montre chacun dans tous ses états ; l'essai tactile fonctionne à la souris ; captures produites et conformes aux jetons.
- Phase courte : elle sert seulement à ce que la maquette soit construite avec les vrais composants.

### Phase 1 — Maquette navigable (proof of concept)

But : voir et toucher **toute** la HMI avec des données de démonstration, sans logique métier réelle (`docs/07` § 9.1).

- Coquille complète : barre du haut (état, mode, OF, override, alarmes, communications, forçages, utilisateur), rail de navigation, bandeau d'alarme, toasts, tiroirs, connexion, carte de navigation de `docs/08` § 4.2 avec les liens profonds (alarme → station, qualité → station, puce Forçages → page Forçages).
- **Toutes les pages** de `docs/08` § 6 avec leur vraie disposition et des données de démonstration crédibles de la machine MX-4 : Machine, Station, Manuel, Alarmes, Qualité, Forçages, Pièces, OF, Studio machine, Studio station, Éditeur de process (niveaux machine et station), Déploiement, Utilisateurs et droits, Statistiques (vide), Communications, Système.
- `IPlcClient` et `PlcSnapshot` (`docs/06` § 7) écrits dès maintenant dans `StdMachine.Core` ; un `DemoPlcClient` les implémente en **rejouant un scénario scripté** de quelques minutes en boucle : la machine démarre, les movers circulent sur le plan, les cartes de « Presser pignon » s'allument dans l'ordre avec un bloc En parallèle, une condition attend, Pressage 01 passe en Aborted puis est réintégrée, une réaction qualité apparaît puis est libérée, un forçage est posé. Les commandes de la barre et des pages changent l'état de démonstration (Démarrer, Pause, Arrêter, Abort, Acquitter).
- **Retours visuels de `docs/08` § 7** sur ces données : cartes en direct (en cours, suivante, attente, erreur, branche non prise), connecteurs animés, suivi de l'exécution, movers interpolés, changements d'état, balises, bandeau d'alarme.
- **Éditeur de process réellement utilisable** au doigt sur le modèle de process de `docs/07` § 3.2 (écrit dès cette phase, sans compilateur ni validation réelle) : palette, couloirs, cartes, blocs, glisser-déposer avec insertion avant/après et branches vides, déplacement, refus expliqués, panneau Étape, annuler/refaire.
- Pavé numérique et clavier dans les formulaires de démonstration (positions, paramètres, OF).
- Bandeau discret « Données de démonstration » dans la barre du haut tant que `DemoPlcClient` est utilisé.
- Scénario de capture `demo` (instantanés à 0 s, 30 s, 60 s, 90 s du scénario) pour `--screenshot-all`.
- **Pas dans cette phase** : catalogue complet, persistance, compilateur, validation réelle, simulateur, droits réels.
- **Critères** : toutes les pages atteignables et remplies ; la démo tourne en boucle sans intervention ; je peux construire un petit process par glisser-déposer au doigt ; captures de toutes les pages.
- **Point de validation du proof of concept** : je teste la maquette sur l'écran tactile réel (aspect, lisibilité, navigation, glisser-déposer, fluidité des animations). Si le glisser-déposer ou le rendu de l'éditeur ne convient pas en WPF, c'est ici qu'on décide de basculer l'éditeur en WebView2. Mes retours sont intégrés aux documents et au journal **avant** la phase 2. Attends ma validation explicite.

### Phase 2 — Domaine, catalogue, projet

- Modèle complet de `docs/07` § 3 dans `StdMachine.Core` (en conservant le modèle de process de la phase 1).
- `catalog/catalog.json` **complet** : tous les types et modules de `docs/03`, avec toutes leurs numérotations, et le corps des fonctions du robot cartésien. Test de cohérence du catalogue.
- Projet d'exemple `samples/MX-4.stdm.json` complet : configuration machine (plan, positions planaires, 6 stations, 6 fonctions dont deux partagées par les stations Pressage 01 et 02, priorités, rebut), objets de chaque station, raccordements (image E/S simulée), positions nommées toutes apprises, variables, opérations de chaque station dessinées comme décrit dans `docs/01`, process général. Le scénario `validation-errors` utilise une copie modifiée de ce projet (une position non apprise, un objet utilisé dans deux branches parallèles).
- Persistance JSON (aller-retour sans perte, migrations) et base SQLite (tables de `docs/07` § 5.2, compte admin initial).
- Toutes les règles de validation de `docs/07` § 4.2, avec liens vers l'élément en cause. Tests.
- Les pages Studio de la maquette passent sur le vrai projet (lecture et modification), la barre de validation sur la vraie validation.

### Phase 3 — Compilateur

- Compilation selon `docs/06` § 5 : cartes, blocs, dépliage des fonctions de module avec substitution des paramètres et décalages d'approche, allocation (variables cachées, conditions, textes, n° locaux, n° d'opérations), verrouillages générés, limites, empreinte SHA-256.
- Carte de compilation (instruction ↔ carte) et listing lisible (onglet Table compilée de l'éditeur).
- Tests : chaque type de carte et de bloc ; le listing de « Presser pignon » a la structure de `docs/06` § 5.6 ; compilation complète de MX-4 sans erreur, en moins de 200 ms.

### Phase 4 — Simulateur

- `SimulatedPlcClient` conforme à `docs/05` et `docs/06` § 7–8 : objets simulés (tous les types du catalogue), contrat d'action, override et timeout, verrouillages, interpréteur à contextes, séquenceurs de station et général, PackML, routage et movers sur plan 2D, canal manuel et jog avec garde, pas à pas, chargeur avec vérifications, alarmes, traces, courbes de presse, OF, persistance des movers entre Stop et Start, forçages (`docs/12`).
- Surveillance des rebuts (`docs/10`) : `FB_QualityMonitor` simulé, imputation, réactions, libération, persistance.
- `ISimulatorControl` et scénarios de capture (`idle`, `running`, `station-aborted`, `step-mode`, `manual`, `validation-errors`, `quality-hold`, `forced`).
- Tests de `docs/07` § 12 (Simulation.Tests), dont la production de 100 pièces en simulation accélérée.
- La HMI démarre désormais sur le simulateur ; `DemoPlcClient` ne sert plus qu'aux tests d'interface.

### Phase 5 — Exploitation sur données réelles

- `SnapshotStore`, `AlarmService` (textes du catalogue et des fichiers de sous-alarmes), `TraceService`, `OrderService` local, `QualityService`.
- Pages Machine, Station, Alarmes, Qualité, Pièces et movers, OF branchées sur le simulateur, avec toutes les commandes réelles et tous les retours visuels de `docs/08` § 7.
- **Critères** : scénario `running` : les movers circulent, les cartes s'allument dans l'ordre, les branches parallèles s'animent ensemble ; scénario `station-aborted` : la production continue sur Pressage 02 pour ses deux fonctions (Pressage pignon et Sertissage), la réintégration de Pressage 01 fonctionne en trois touchers ; la file d'une station de pressage sert d'abord les movers venus pour Sertissage.

### Phase 6 — Studio complet

- Studio machine, Studio station, Éditeur de process et Déploiement branchés sur le projet, le compilateur et le simulateur, avec enregistrement automatique, liens profonds, chronogramme estimé, mode « Suivre l'exécution » (même rendu que la page Station), fonctions personnalisées (copier une fonction du robot, la modifier, l'utiliser).
- **Critères** : je peux ajouter une étape « Attendre condition » dans Presser pignon, la valider, déployer sur le simulateur et la voir s'exécuter en direct ; un objet glissé dans deux branches parallèles est refusé avec un message clair.

### Phase 7 — Manuel, pas à pas et forçages

- Page Manuel (`docs/08` § 6.3) générée depuis le catalogue : actions, paramètres, résultats, jog maintenu, override manuel, vue XY du robot, apprentissage des positions, journal.
- Pas à pas dans la page Station (granularités Opération / Étape / Action, bouton Pas suivant, branches parallèles, Abort).
- Page Forçages (`docs/12`) branchée : états déclarés et démarrage bloqué, qualification des arrêts, station hors production (impact affiché, Maintenance seule, remise en production en trois étapes), mover hors service, forçages d'essai en simulation.
- **Critères** : scénario `manual` ; une position apprise en manuel apparaît dans le projet et dans le simulateur ; scénario `forced` : Pressage 01 hors production en Maintenance, mode manuel possible sur elle pendant que la machine produit avec Pressage 02 ; un état de maintenance déclaré refuse le Démarrer.

### Phase 8 — Utilisateurs, droits, système, communications

- Connexion (identifiant + mot de passe ou PIN), niveaux, droits de `docs/02` § 17 appliqués partout (cadenas, connexion à la demande), journal, déconnexion automatique.
- Page Système (simulateur, projet, export des constantes PLC `GVL_Catalog.TcGVL`, affichage), page Statistiques vide.
- Interface usine, **première version seulement** (`docs/11` § 7.1) : projet `StdMachine.Plant` avec les contrats (`IPlantChannel`, `IPlantEventChannel`, `IPlantGateway`), le modèle des messages et leurs schémas JSON, `PlantGateway`, le canal Journal de messages, les canaux MQTT et OPC UA déclarés « Prévu » ; page Communications (`docs/08` § 6.16) branchée, réglages enregistrés, secrets chiffrés, tests. Pas de MQTT ni d'outbox pour l'instant.
- Revue finale du design : `--screenshot-all` sur tous les scénarios, liste des écarts restants.

### Phase 9 — PLC et ADS (seulement sur demande explicite)

- Bibliothèque `StdMachineLib` en ST (fichiers `.TcPOU`, `.TcDUT`, `.TcGVL`, `.TcIO` importables dans un projet TwinCAT 3 que je créerai) selon `docs/04` à `docs/06`, tests TcUnit reprenant les cas des tests du simulateur.
- `StdMachine.Plc.Ads` : `AdsPlcClient` (Beckhoff.TwinCAT.Ads), sérialisation `pack_mode 1`, lectures groupées, handshakes.
- `GVL_Plant` et `FB_PlantPublisher` (`docs/11` § 3), avec les attributs OPC UA pour le serveur TwinCAT.

### Phase 10 — Interface usine réelle (seulement sur demande explicite)

- Canal MQTT (MQTTnet) avec outbox, états conservés, `PlantOrderSource`, tests avec le broker MQTTnet embarqué (`docs/11` § 4, 7.2, 8).

## À la fin de chaque phase

- `dotnet build` sans avertissement, `dotnet test` vert.
- Captures dans `artifacts/screens/` (non versionnées), regardées et comparées.
- Commit(s) sur `develop`. Pas de `git push`.
- Compte rendu : fait, reste, écarts au design, décisions ajoutées au journal, commande pour lancer l'aperçu (`dotnet run --project src/StdMachine.Hmi`).
