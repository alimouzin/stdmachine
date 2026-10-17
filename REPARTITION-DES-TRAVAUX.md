# Répartition des travaux entre deux agents

Écrit le 2026-10-17. Point de départ commun : branche `feature/verification-simulateur` au commit `82ac1ca` (poussée). Ce fichier se lit après `CLAUDE.md` et `ETAT-ET-PROCHAINS-CHANTIERS.md`.

## Principe

- **Agent A (cette session, Linux, sans compilateur)** : spécification seulement. Il possède `docs/` (sauf `docs/09`, voir plus bas) et ne touche à aucun fichier de `src/`, `tests/`, `catalog/`, `samples/`.
- **Agent B (Windows, compile et teste)** : code seulement. Il possède `src/`, `tests/`, `catalog/`, `samples/`, `tools/`. Il ne modifie pas `docs/` 00 à 08 et 10 à 13 ; quand le code révèle un défaut ou un manque dans un document, il l'écrit dans `docs/09` (journal) et dans la section « Écarts constatés par le code » ci-dessous, et A le reporte dans le document.
- Branches : A reste sur `feature/verification-simulateur`. B travaille sur `feature/code-fin-des-pieces` créée depuis le même commit (puis une branche par chantier si utile). Pas de conflit de fichiers, donc fusion simple dans `develop` à la fin.
- Journal `docs/09` : A numérote ses décisions à partir de **D80**, B à partir de **D100**. Le fichier est en ajout seulement.

## Ce que prend l'agent A (spécification, pas de code)

1. **Variables de mover** : compléter `docs/05` (structure `ST_MoverCtx`, `SetVar`, remise à zéro, persistance), `docs/06` (`E_ValueSource.MoverData`, `E_BindTarget.MoverData`, tableaux dans la structure du mover), `docs/03` si le catalogue est touché, `docs/07` (modèle et règles de validation), `docs/08` (tiroir du mover, déclaration dans le Studio machine). Aujourd'hui seulement marquées « prévu » dans les documents.
2. **Sorties des blocs composés** (fonctions personnalisées, sous-process, opérations) : passer la proposition de `docs/13` §2 en spécification ferme dans `docs/02`, `06`, `07`, `08`, avec les règles de validation et les cas limites.
3. **Relecture de cohérence** des ajouts du 2026-10-17 (Fin des pièces, Préparation, Calibrations, Flux) : numéros d'instructions et de règles, tableaux de limites (`docs/05` §11), états de balise, droits (`docs/02` §17), exemples des scénarios (`docs/05` §12), `docs/13` marqué « réalisé » ou archivé.

Livrable A : documents à jour, décisions D80 et suivantes, et une liste de cas de test par chantier (dans `docs/07` §12) que B pourra coder.

## Ce que prend l'agent B (code, avec compilateur)

Dans cet ordre, un commit par étape, `dotnet build` sans avertissement et `dotnet test` vert à chaque fois :

1. **Fin des pièces en cours** (`docs/02` §8, `docs/05` §5.7, D77). Court. `bDrain` dans le simulateur (réutiliser le verrou de la fin d'OF de `MachineSim`), commandes `DrainStart` / `DrainCancel`, instantané (`bDrain`, pièces en cours), bouton « Finir les pièces », bandeau et toast dans la page Machine, scénario de capture, tests.
2. **Préparation d'une opération** (`docs/02` §5.1–5.2, `docs/05` §4.1–4.2 bis, `docs/06`, D78). Étape `WaitMoverStep`, compilation (`WaitMover`, `bPrepare`), validation V021, V022, V116, simulateur (demande à la réservation, statut `Preparing`, pause, perte au Stop), éditeur (séparateur, zone teintée), tests, mesure du gain de cycle en simulation accélérée.
3. **Calibrations** (`docs/02` §11.1, `docs/05` §10.1, `docs/06`, D79). Types de process, étapes `Prompt`, `SavePos`, `CalibStation`, variables persistantes, validité et alarme 912, onglet Calibrations de la page Manuel, tiroir de demande, tests.
4. **Utilisateurs, droits, mode manuel complet** (`ETAT-ET-PROCHAINS-CHANTIERS.md`, chantier 3) : connexion réelle, cadenas, apprentissage branché sur `TeachPositionAsync`, jog maintenu, pas à pas.
5. **Variables de mover** et **sorties des blocs composés**, seulement quand A a livré leurs spécifications fermes (point 1 et 2 ci-dessus).

Les étapes 1 à 4 ont leur spécification complète : B peut commencer tout de suite. Le catalogue `catalog/catalog.json` reste la seule source des numéros (aucun numéro en dur) ; les nouvelles instructions de `docs/06` (17 `WaitMover`, 18 `Prompt`, 19 `SavePos`, 21 `CalibStation`) sont des valeurs de `E_Op`, pas du catalogue.

## Écarts constatés par le code

(B ajoute ici, en ajout seulement : document, section, problème, solution retenue.)

## Rappel des règles du dépôt

Conventions de `CLAUDE.md` : petits commits en français (Conventional Commits), pas de `git push` sans demande explicite, captures regardées après toute modification d'interface, textes affichés dans `Resources/Strings.resx`, aucune valeur en dur dans les vues.
