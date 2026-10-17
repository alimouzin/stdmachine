# État d'avancement et prochains chantiers

Dernière mise à jour : 2026-10-17. Branche `feature/verification-simulateur` (issue de `develop`, 14 commits d'avance). À lire après `CLAUDE.md` ; les décisions détaillées sont dans `docs/09-journal-des-decisions.md` (D67 à D75 viennent de cette session) et la conception des sorties et variables dans `docs/13-proposition-couloirs-et-sorties.md` (proposition, ne fait pas foi tant qu'elle n'est pas validée).

## Où on en est

**Base** (`develop`) : HMI WPF (.NET Framework 4.8) contre un simulateur fidèle du PLC, machine d'exemple MX-4. Catalogue JSON complet, modèle de projet avec persistance JSON, validation V001 à V115, compilateur vers des tables, simulateur (PackML, séquenceurs, movers, pièces, rebuts, forçages), interface usine (journal de messages), droits et sessions dans `Core/Security`. 22 pages. Environ 22 000 lignes de C# (HMI 10 600, simulateur 6 500, `Core` 3 500, interface usine 1 000) et 2 200 lignes de tests : **182 tests verts**, 0 erreur de compilation, 43 avertissements `xUnit1031` dans les tests.

**Fait pendant cette session**
- **Vérification sur Windows** : build, tests, lancement, 176 captures (8 scénarios × 22 pages) regardées.
- **Corrections** : fonctions de module en mode manuel (D67) ; pages Machine, Qualité et Déploiement alimentées par l'instantané réel (D68) ; scénario `manual` à l'état Stopped (D69) ; commutateurs `Segmented` et onglets `Tabs` qui ne répondaient pas à la souris (D71).
- **Éditeur de process, mode Flux** : une seule colonne, pastille d'objet sur chaque carte, sans couloirs. C'est le mode par défaut (D72) ; les couloirs restent en option, leur suppression est à décider plus tard.
- **Variables nommées** (étape 1 de `docs/13`) : variables de process et de station, création à la demande depuis les sorties d'une étape, cartes « Résultat → variable », onglet Données, annuler / refaire, validation V020 et V115 (D73 à D75).

## Les 3 prochains gros chantiers

### 1. Variables de mover (spécifiées de façon ferme le 2026-10-17, D80 ; pas encore codées)
Des informations propres au mover, lisibles et modifiables par les stations (offset du mover, réglage choisi en amont, compteur, drapeau…). Conception dans `docs/13` § 5.
- Liste déclarée dans la configuration machine : nom, type **nombre, booléen ou position**, unité, valeur initiale, 16 au plus ; chaque mover a son jeu de valeurs.
- Remise à zéro **au choix par variable** : jamais (survit au Stop, au Reset, au redéploiement), au déploiement, ou quand le mover est vidé ; une étape « Réinitialiser » existe aussi.
- Accès par les opérations de station (mover présent au poste) et par le process général ; refusé dans le cycle de fond.
- À faire : modèle, compilation (source et cible `MoverData`), simulateur, validation, tiroir du mover dans la HMI, déploiement qui conserve les valeurs par identifiant, et mise à jour de `docs/05` et `docs/06` (structure du mover, `SetVar`, sources et cibles).
- Taille : un chantier de plusieurs jours, comparable à l'étape 1 des variables nommées, avec en plus un changement du contrat PLC.

### 2. Sorties des blocs composés (spécifiées de façon ferme, D81 ; pas encore codées)
Étendre le mécanisme de l'étape 1 aux fonctions personnalisées (dépliées à la compilation, sans coût d'exécution), puis aux sous-process et aux opérations (valeur passée par un emplacement interne, copiée vers la variable nommée choisie à l'appel). Plusieurs sorties par bloc, nommées ou non (une sortie sans nom se désigne par son rang). Exemple visé : un bloc « Rechercher pièce Asyril » (chercher avec reprises, puis obtenir) qui rend une position et un nombre.
- À faire : modèle des sorties de bloc, choix de la variable à l'appel dans l'éditeur, compilation, règle « un bloc à sorties ne s'appelle pas depuis deux contextes à la fois », validation, tests, simulation de bout en bout.
- Ensuite, **objets à sorties variables** (Keyence XG) : table de sorties déclarée sur l'objet, indicateur `variableOutputs` dans le catalogue, simulation à partir d'une plage. Remis à plus tard : il faut d'abord connaître le nombre, les types et le mode de transmission des valeurs.
- Limite à connaître : le PLC garde 8 résultats par action, avec une seule position et un seul texte.

### 3. Utilisateurs, droits et mode manuel complet
La HMI affiche encore des utilisateurs de démonstration (tout PIN à 4 chiffres est accepté). Le code de `Core/Security` est prêt et testé : 35 droits, niveaux, mots de passe PBKDF2, déconnexion automatique.
- À faire : connexion réelle, cadenas et droits appliqués partout, page Utilisateurs et droits sur les vraies données (phase 8).
- Mode manuel : apprentissage des positions branché sur `TeachPositionAsync` (aujourd'hui « non enregistré »), jog maintenu, pas à pas dans la page Station (phase 7).
- Le paquet SQLite (`Microsoft.Data.Sqlite`) est à valider avant toute persistance en base.

### 0. Fin des pièces en cours (spécifiée le 2026-10-17, D77, pas codée)
Mode d'arrêt « Finir les pièces » : plus de chargement, les pièces engagées finissent, puis Stop automatique. Spécification : `docs/02` § 8, `docs/05` § 5.7. À faire : commandes `DrainStart` / `DrainCancel` dans le contrat (`PlcSnapshot`, `IPlcClient`), indicateur `bDrain` et compte des pièces en cours dans le simulateur (réutiliser le verrou de la fin d'OF dans `MachineSim`), bouton, bandeau et toast dans la page Machine, scénario de capture, tests (arrêt automatique quand la dernière pièce est sortie ; annulation ; Stop et Abort lèvent le mode ; un mover à vider retient la fin). Chantier court : à faire en premier.

### 0 bis. Préparation d'une opération (spécifiée, D78, pas codée)
Étape « Mover requis » : ce qui la précède tourne dès la réservation de la position de travail. Spécification : `docs/02` § 5.1–5.2, `docs/05` § 4.1–4.2 bis, `docs/06` (`WaitMover`, `bPrepare`). À faire : modèle (`WaitMoverStep`), compilation, validation V021 / V022 / V116, simulateur (demande à la réservation, statut `Preparing`, reprise après Pause, perte au Stop), éditeur (carte séparateur, zone teintée, sélecteurs sans données de pièce), chronogramme, scénario et tests (gain de cycle mesuré en simulation accélérée).

### 0 ter. Calibrations (spécifiées, D79, pas codées)
Process Calibration de station et machine, variables persistantes, onglet Calibrations de la page Manuel, avertissement 912. Spécification : `docs/02` § 11.1, `docs/05` § 10.1, `docs/06`. À faire : modèle (types de process, étapes, `Retained`, validité), compilation, validation V023 à V025, simulateur (copie de travail validée à la fin, `Prompt`, `CalibStation`, état et date), persistance de la mémoire rémanente, HMI (onglet, tiroir de demande, éditeur), tests.

**Ensuite** (non planifié) : Studio complet branché sur le projet réel avec enregistrement automatique et chronogramme (phase 6) ; détail des différences au déploiement (aujourd'hui seulement l'empreinte) ; PLC réel et ADS (phase 9, sur demande explicite) ; interface usine MQTT (phase 10).

## Points ouverts et dettes connues
- **Documents de référence** : mis à jour le 2026-10-17 (D76) pour le mode Flux, les variables de process et les sorties. Les variables de mover y sont marquées « prévu ». Reste : `docs/05` (structure `ST_Mover` et `SetVar` détaillés) et `docs/03` quand ce chantier sera codé. Mise à jour faite sans compilateur, relire.
- **Couloirs** : à garder ou à supprimer, décision remise à plus tard (note dans `REPRISE-PHASES-2-A-5.md`).
- **Qualité** : une station verrouillée par imputation n'affiche pas son verrou sur sa ligne.
- **Pages encore en données de démonstration** : Utilisateurs, Communications, Système, historique de la page Station.
- **Mode manuel** : les axes doivent être référencés (« Référencer ») avant tout mouvement ; l'apprentissage des positions n'est pas branché.
- **Captures** : limitées à la taille de l'écran en unités WPF (D70) ; sur cet écran (2 560 × 1 440 à 150 %), la mise en page fait 1 724 × 977.
- **Tests** : 43 avertissements `xUnit1031` (tests synchrones qui attendent une tâche) ; pas de test automatique de la mise en page de l'éditeur (le projet de tests ne référence pas la HMI).
- **Dépôt** : la branche `garage` contient l'ancien aperçu .NET 8 (sans ancêtre commun avec `develop`), à garder comme archive ou à supprimer ; la branche locale `main` ne doit pas être poussée. Un dossier `HMI/` non suivi traîne à la racine (restes de compilation de cet ancien aperçu), exclu localement.
- **Build** : le dépôt est dans OneDrive, qui verrouille parfois les fichiers intermédiaires ; en cas d'erreur `BG1002` ou de ressource `app.xaml` introuvable, supprimer `src/StdMachine.Hmi/obj` et `bin` puis `powershell -File tools\build.ps1 -Restore`.

## Commandes utiles
- Construire : `powershell -File tools\build.ps1 -Restore`
- Tests : `vstest.console.exe` sur les 4 DLL `tests\StdMachine.*.Tests\bin\Debug\net48\*.Tests.dll` ; `STDM_UPDATE_SAMPLES=1` régénère `samples/*.json` (après avoir compris pourquoi ils diffèrent).
- Lancer : `src\StdMachine.Hmi\bin\Debug\net48\StdMachine.Hmi.exe --simulator` (`--windowed`, `--scenario <nom>`, `--layout lanes|flow`, `--page <clé>`).
- Captures : `--simulator --scenario <nom> --screenshot-all artifacts\screens`.
