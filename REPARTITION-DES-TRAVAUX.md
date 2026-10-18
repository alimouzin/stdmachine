# Répartition des travaux entre deux agents

Écrit le 2026-10-17. Point de départ commun : branche `feature/verification-simulateur` au commit `82ac1ca` (poussée). Ce fichier se lit après `CLAUDE.md` et `ETAT-ET-PROCHAINS-CHANTIERS.md`.

## Principe

- **Agent A (cette session, Linux, sans compilateur)** : spécification seulement. Il possède `docs/` (sauf `docs/09`, voir plus bas) et ne touche à aucun fichier de `src/`, `tests/`, `catalog/`, `samples/`.
- **Agent B (Windows, compile et teste)** : code seulement. Il possède `src/`, `tests/`, `catalog/`, `samples/`, `tools/`. Il ne modifie pas `docs/` 00 à 08 et 10 à 13 ; quand le code révèle un défaut ou un manque dans un document, il l'écrit dans `docs/09` (journal) et dans la section « Écarts constatés par le code » ci-dessous, et A le reporte dans le document.
- Branches : A reste sur `feature/verification-simulateur`. B travaille sur `feature/code-fin-des-pieces` créée depuis le même commit (puis une branche par chantier si utile). Pas de conflit de fichiers, donc fusion simple dans `develop` à la fin.
- Journal `docs/09` : A numérote ses décisions à partir de **D80**, B à partir de **D100**. Le fichier est en ajout seulement.

## Ce que prend l'agent A (spécification, pas de code)

1. **Variables de mover** *(fait le 2026-10-17, D80 : B peut les coder, `docs/05` § 5.8)* : compléter `docs/05` (structure `ST_MoverCtx`, `SetVar`, remise à zéro, persistance), `docs/06` (`E_ValueSource.MoverData`, `E_BindTarget.MoverData`, tableaux dans la structure du mover), `docs/03` si le catalogue est touché, `docs/07` (modèle et règles de validation), `docs/08` (tiroir du mover, déclaration dans le Studio machine). Aujourd'hui seulement marquées « prévu » dans les documents.
2. **Sorties des blocs composés** *(fait le 2026-10-17, D81 : `docs/02` § 5.5)* (fonctions personnalisées, sous-process, opérations) : passer la proposition de `docs/13` §2 en spécification ferme dans `docs/02`, `06`, `07`, `08`, avec les règles de validation et les cas limites.
3. **Relecture de cohérence** *(faite le 2026-10-17 : règles de validation renumérotées — erreurs V0xx jusqu'à V029, avertissements V1xx jusqu'à V121 —, droits `calibration.run` et `mover.var.edit` ajoutés, balise « machine vidée »)* des ajouts du 2026-10-17 (Fin des pièces, Préparation, Calibrations, Flux) : numéros d'instructions et de règles, tableaux de limites (`docs/05` §11), états de balise, droits (`docs/02` §17), exemples des scénarios (`docs/05` §12), `docs/13` marqué « réalisé » ou archivé.

Livrable A : documents à jour, décisions D80 et suivantes, et une liste de cas de test par chantier (dans `docs/07` §12) que B pourra coder.

## Ce que prend l'agent B (code, avec compilateur)

Dans cet ordre, un commit par étape, `dotnet build` sans avertissement et `dotnet test` vert à chaque fois :

1. **Fin des pièces en cours** (`docs/02` §8, `docs/05` §5.7, D77). Court. `bDrain` dans le simulateur (réutiliser le verrou de la fin d'OF de `MachineSim`), commandes `DrainStart` / `DrainCancel`, instantané (`bDrain`, pièces en cours), bouton « Finir les pièces », bandeau et toast dans la page Machine, scénario de capture, tests.
2. **Préparation d'une opération** (`docs/02` §5.1–5.2, `docs/05` §4.1–4.2 bis, `docs/06`, D78). Étape `WaitMoverStep`, compilation (`WaitMover`, `bPrepare`), validation V021, V022, V116, simulateur (demande à la réservation, statut `Preparing`, pause, perte au Stop), éditeur (séparateur, zone teintée), tests, mesure du gain de cycle en simulation accélérée.
3. **Calibrations** (`docs/02` §11.1, `docs/05` §10.1, `docs/06`, D79). Types de process, étapes `Prompt`, `SavePos`, `CalibStation`, variables persistantes, validité et alarme 912, onglet Calibrations de la page Manuel, tiroir de demande, tests.
4. **Utilisateurs, droits, mode manuel complet** (`ETAT-ET-PROCHAINS-CHANTIERS.md`, chantier 3) : connexion réelle, cadenas, apprentissage branché sur `TeachPositionAsync`, jog maintenu, pas à pas.
5. **Variables de mover** et **sorties des blocs composés**, seulement quand A a livré leurs spécifications fermes (point 1 et 2 ci-dessus).

Les étapes 1 à 4 ont leur spécification complète : B peut commencer tout de suite. Le catalogue `catalog/catalog.json` reste la seule source des numéros (aucun numéro en dur) ; les nouvelles instructions de `docs/06` (17 `WaitMover`, 18 `Prompt`, 19 `SavePos`, 21 `CalibStation`) sont des valeurs de `E_Op`, pas du catalogue.

## État au 2026-10-17 (fin de la session de spécification)

Les trois points de l'agent A sont faits. Instructions ajoutées à `E_Op` : 17 `WaitMover`, 18 `Prompt`, 19 `SavePos`, 21 `CalibStation`, 22 `ResetMoverVars`, 23 `SetOut`. Règles ajoutées : V021 à V029, V116 à V119, V121. Décisions : D77 à D81. Tout le travail de code des documents ci-dessus (fin des pièces, préparation, calibrations, variables de mover, sorties de blocs, droits) reste pour l'agent B ; l'ordre conseillé : fin des pièces, préparation, variables de mover, sorties de blocs, calibrations, droits.

## Ajouts ultérieurs (agent A)

- D82 : « Finir les pièces » aussi depuis Idle.
- D83 : plusieurs OF commencés, un actif, changement à chaud (`docs/02` § 16, `docs/05` § 5.6) ; source `OrderParam` := 11 ; table d'OF `aOrder[1..8]`. Chantier de code à placer après la préparation et avant les calibrations.

- D84 : bibliothèque de fonctions au niveau machine (`docs/02` § 5.6, règles V030 et V031). Chantier de code : modèle `FunctionLibrary`, objets par rôle, compilation, publication, Studio machine, tests. À placer avant les sorties de blocs.

- D85 : relecture complète, voir `REVUE-DU-PROJET.md` § 2 pour les manques du code (priorité 1 : enregistrement du projet, base de données, droits, paramètres d'OF vers le PLC) ; à traiter **avant** les nouveaux chantiers, en commençant par la décision .NET 10 / net48.

- D86 à D90 (2026-10-18) : frames transformés seulement par les robots Stäubli (règle V122) ; fenêtres qualité au changement de référence ; **migration vers .NET 10 et SQLite acceptées : c'est la première tâche de l'agent B**, avant l'enregistrement du projet et la base ; compatibilité HMI–PLC (`docs/02` § 7.3, V032, V202) ; sauvegarde des valeurs du PLC (`docs/02` § 7.4).

**Ordre conseillé pour l'agent B, mis à jour** : 1. migration .NET 10 ; 2. enregistrement du projet et base SQLite (`REVUE-DU-PROJET.md` § 2, priorité 1) ; 3. droits ; 4. paramètres d'OF et OF à chaud ; 5. fin des pièces ; 6. préparation ; 7. variables de mover ; 8. bibliothèque de fonctions ; 9. sorties de blocs ; 10. calibrations ; 11. compatibilité et sauvegarde des valeurs du PLC.

- D91 : alarmes (`docs/14-alarmes.md`). À coder : code complet dans `Alarm` (carte = 1000 + n, compilateur et simulateur : `StationSim.cs:809`, `MachineSim.cs:491`), 902 → 905 pour un PC invalide dans `StationSim.cs:472`, nouveaux codes 905 à 917, alarmes de la HMI 9000 à 9099, champs `severity`, `cause`, `remedy` du catalogue, fichiers `catalog/subalarms/`, historique par identifiant stable, V033 et V123. À placer avec la base SQLite (point 2 de l'ordre).

- D92 : **l'agent A écrit aussi la bibliothèque PLC** (`plc/`, sans TwinCAT, à l'aveugle). L'agent B n'y touche pas ; s'il a TwinCAT, il peut l'importer et signaler les erreurs de compilation dans la section ci-dessous. Les structures d'échange de `plc/src/01_Types` font foi pour le futur client ADS.

- D93, D94 (PLC) : pour le futur client ADS et le simulateur, `ST_MachineStatus` gagne `eStopReason` (`E_StopReason`) et `bHmiLost` ; codes d'erreur de déploiement 1 à 22 en tête de `plc/src/05_Sequencers/FB_Loader.st` ; nouveau code d'alarme 909. Écarts de `05`/`06` adoptés par le PLC comme par le simulateur (`Launch` sans passage en Execute, `SetOut aArg[2]`, `SavePos aArg[2..5]`, compteurs de boucle cachés au niveau machine).

## Écarts constatés par le code

(B ajoute ici, en ajout seulement : document, section, problème, solution retenue.)

## Rappel des règles du dépôt

Conventions de `CLAUDE.md` : petits commits en français (Conventional Commits), pas de `git push` sans demande explicite, captures regardées après toute modification d'interface, textes affichés dans `Resources/Strings.resx`, aucune valeur en dur dans les vues.
