# Reprise : phases 2 à 5 — instructions pour la session Windows

Dernière mise à jour : 2026-10-16. Branche `develop` (remote `origin`). Ce fichier est écrit pour **Claude Code lancé sur le poste Windows** après un `git pull`. Lis d'abord `CLAUDE.md`, `POC-NET48.md` (net48 prime sur .NET 10 pour le PoC), puis ce fichier. Décisions : `docs/09-journal-des-decisions.md` (D49 à D66 viennent de cette session). Commits en français (Conventional Commits) avec le trailer habituel ; pas de `git push` sans demande explicite de l'utilisateur.

## Contexte
Une session distante Linux (sans Windows ni WPF en exécution) a écrit les phases 2, 3, 4 et une partie des phases 5 et 8. Elle a vérifié : 160 tests verts sous .NET 8 **et** sous .NET Framework 4.8 (Mono 6.8, `xunit.console -parallel none`), et la compilation de la HMI (`dotnet build src/StdMachine.Hmi -p:EnableWindowsTargeting=true`, 0 avertissement). **La HMI n'a jamais été lancée**, ni les captures d'écran produites : c'est ton travail ici.

## À faire, dans l'ordre
1. **Compiler** : `powershell -File tools\build.ps1 -Restore`. Doit donner 0 erreur, 0 avertissement dans `Core`, `Persistence`, `Simulation`, `Plant`. Nouveaux paquets NuGet : `System.Text.Json` 8.0.5 (via `Core`), `xunit` 2.9.2, `Microsoft.NET.Test.Sdk` 17.11.1, `xunit.runner.visualstudio` 2.8.2 (tests). Si la restauration échoue : le signaler, ne pas changer de paquet sans demander. Nouvelles références du projet HMI : `StdMachine.Persistence`, `StdMachine.Plant`.
2. **Tests** : ouvrir `StdMachine.sln` dans Visual Studio (Explorateur de tests) ou lancer `vstest.console.exe` sur les 4 DLL `tests\StdMachine.*.Tests\bin\Debug\net48\*.Tests.dll`. Attendu : Core 62, Persistence 7, Plant 7, Simulation 83, tous verts. Un échec « samples/MX-4.stdm.json n'est plus à jour » se corrige avec la variable d'environnement `STDM_UPDATE_SAMPLES=1` (le fichier est produit par `SampleProject`), mais d'abord comprendre pourquoi il diffère (sur Linux net8 et net48 il est identique octet pour octet).
3. **Lancer le simulateur** : `src\StdMachine.Hmi\bin\Debug\net48\StdMachine.Hmi.exe --simulator --windowed` (autres options : `--scenario <nom>`, `--plant-log`, `--page <clé>`, `--topmost`). Le catalogue et le projet d'exemple sont copiés à côté de l'exe (`catalog\catalog.json`, `samples\MX-4.stdm.json`). Machine arrêtée au départ : Réarmer, puis Démarrer dans la barre du haut. Vérifier que les movers circulent, que les cartes de « Presser pignon » s'allument dans l'ordre, qu'un Abort de station laisse l'autre presse produire, que Clear → Reset → Start réintègre la station.
4. **Captures** : `StdMachine.Hmi.exe --simulator --scenario <nom> --screenshot-all artifacts\screens` pour chacun des scénarios `idle`, `running`, `station-aborted`, `step-mode`, `manual`, `validation-errors`, `quality-hold`, `forced` (dossier `sim-<scénario>`). **Regarder les images** et les comparer à `docs/08` et à la maquette (règle de `CLAUDE.md`).
5. **Corriger ce qui casse.** Points à surveiller en priorité (écrits sans pouvoir exécuter) :
   - Page **Qualité** (fenêtres en `0`/`1`, `Machine` toujours présent), page **Station** (cartes, contextes, historique vide en mode simulateur), page **Machine** (movers en mm), **Forçages**, **OF**, **Alarmes** (les erreurs traitées par « Refaire » apparaissent comme informations déjà levées).
   - **Déploiement** : compile le projet réel puis appelle `SimulatedPlcClient.Deploy` ; refusé si la machine n'est pas Stopped.
   - Onglet **Table compilée** de l'éditeur de process (listing en police `Font.Mono`).
   - Page **Manuel** : les fonctions de module (« Aller à »…) répondent « Action inconnue » sur le simulateur ; les actions de base marchent. À corriger (le simulateur doit déplier la fonction ; la page envoie aujourd'hui des arguments par défaut, pas la position choisie).
   - Pages encore en données de démonstration : Utilisateurs, Communications, Système, historique de la page Station.
6. Mettre à jour ce fichier et `POC-REPRISE.md`, ajouter les décisions au journal, committer.

## Ce qui est fait (rappel)
- **Phase 2** : `catalog/catalog.json` (28 types, 2 modules, fonctions du robot en arbre d'étapes) ; modèle complété (E/S, matériel, recettes de presse, verrouillages, qualité) ; `samples/MX-4.stdm.json` et `samples/MX-4.validation-errors.stdm.json` ; JSON versionné avec migrations (`StdMachine.Persistence`) ; validation V001 à V113 (`Core/Validation`).
- **Phase 3** : `ProjectCompiler` → `CompiledTables` (`Core/Compiler`) : cartes, blocs, fonctions de module dépliées, conditions, textes, variables cachées, verrouillages générés, carte de compilation, `Listing.Of`, empreinte SHA-256.
- **Phase 4** : `StdMachine.Simulation/Engine` (`MachineSim`, `StationSim`, `SimObject`, `QualityMonitor`) et `SimulatedPlcClient` ; scénarios dans `SimScenarios`. Détail : D59 à D63.
- **Phase 5 (partielle)** : instantanés réels dans la HMI via `--simulator`.
- **Phase 8 (partielle)** : `StdMachine.Plant` (14 schémas, passerelle, canal Journal de messages, réglages avec secrets chiffrés, `seq` persistant ; D64, D65) ; droits, niveaux, PBKDF2 et session dans `Core/Security` (D66).

## Pièges connus
- `Directory.Build.props` impose `TreatWarningsAsErrors` à Core, Simulation, Persistence, Plant.
- Les projets de tests visent net48 sous Windows et net8.0 ailleurs (sources liées) ; un fichier de test ne doit pas dépendre de la plateforme.
- Le fichier projet doit rester identique sur toutes les plateformes : les réels sont écrits par `ShortestDoubleConverter` (sans lui, .NET Framework écrit `0.10000000000000001`).
- `ObjectStatus`, `QualityTrack`, etc. : la HMI de la maquette attend des instantanés complets dès le démarrage (ex. `tracks.First(Machine)` dans la page Qualité).
- Un objet ne peut pas être commandé par deux contextes à la fois (le second est refusé, code 3) : le cycle de fond de l'exemple n'utilise donc pas la vision.

## Reste à faire (hors vérification)
- **Phase 6** (Studio complet branché sur le projet réel, enregistrement automatique, chronogramme estimé), **phase 7** (page Manuel complète, pas à pas, Forçages en pages réelles), **phase 8** (utilisateurs/droits dans la HMI, page Communications branchée sur `PlantGateway`, SQLite — paquet `Microsoft.Data.Sqlite` à demander avant ajout), phases 9 et 10 sur demande seulement.
- Règles de validation non implémentées faute de modèle : V014, V106, V114, V201.
- Pas de collisions ni de trajets planaires (lignes droites), pas de courbes de presse, pas de sécurités simulées.

## Vérification sur le poste Windows (2026-10-16, soir) — faite
- **Compilation** : `tools\build.ps1 -Restore` : 0 erreur, 0 avertissement dans `Core`, `Persistence`, `Simulation`, `Plant`, `Hmi` ; 43 avertissements `xUnit1031` dans les projets de tests (tests synchrones qui attendent une tâche).
- **Tests** : 162 verts (Core 62, Persistence 7, Plant 7, Simulation 86) avec `vstest.console.exe` sur les 4 DLL net48.
- **Lancement** : la HMI démarre en `--simulator` sans erreur ; 8 scénarios × 22 pages = 176 captures sans erreur (`artifacts\screens\sim-<scénario>`), regardées pour Machine, Station, Manuel, Qualité, Forçages et Déploiement.
- **Corrigé** (D67 à D69) : fonctions de module en manuel ; page Manuel (valeurs choisies envoyées, journal sans lignes factices, échec marqué d'une croix) ; page Qualité (répartition des rebuts calculée) ; page Machine (durée dans l'état, carte OF vide) ; page Déploiement (version déployée réelle, différences par empreinte, message de validation réussie) ; scénario `manual`.
- **Pas un défaut** : les états PackML restent en anglais dans l'interface (glossaire, `docs/00` ligne « États »). Les captures sont rognées sur ce poste (D70).

## Reste après cette vérification
- Page Qualité : une station verrouillée par imputation (échelle `StationImputed`) n'affiche pas son verrou sur sa ligne ; il n'apparaît que dans « Verrous en cours ».
- Pages encore en données de démonstration : Utilisateurs (tout PIN à 4 chiffres accepté), Communications, Système, historique de la page Station ; apprentissage des positions en manuel non branché sur `TeachPositionAsync`.
- Capture `step-mode` : la station concernée est Pressage 02 (`Station-3`), pas `Station-2`.

## Sujets de conception à traiter ensuite (demande du 2026-10-16)
1. **Couloirs (swimlanes)** : sont-ils utiles pour programmer les stations et la machine ? Une présentation sans couloirs serait peut-être plus lisible. À étudier avant toute modification de `docs/07` § 3.2 et `docs/08` § 6.9.
2. **Sorties de bloc** : une étape ou un bloc doit pouvoir **définir des sorties** (résultats nommés et typés, utilisables par les étapes suivantes). Exemples : une recherche de pièce Asyril fournit une position ; une caméra Keyence XG a des sorties variables à définir par l'ingénieur process. Aujourd'hui les résultats viennent seulement du catalogue (`CatalogAction.Results`) et se lient par `ActionStep.Bindings`. À concevoir : modèle (sorties déclarées par l'étape ou par l'objet, types, portée), catalogue (objets à sorties variables), compilation (variables cachées), validation (V014 liée), éditeur (panneau Étape).
