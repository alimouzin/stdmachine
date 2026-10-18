# Standard de machine de production — HMI C# (WPF) + PLC Beckhoff TwinCAT 3

Le but de ce dépôt est un standard pour des machines de production autour d'un système planaire Beckhoff : une machine dont le logiciel est le plus flexible possible. Une nouvelle machine se définit dans la HMI (stations, objets, process dessinés par glisser-déposer), est compilée en tables et exécutée par un PLC générique.

## Documents de référence

Lire `docs/README.md`, puis les documents dans l'ordre. Ils font foi :

- `docs/00-glossaire.md` : vocabulaire obligatoire (code et interface). Ne jamais employer un mot de la liste « à éviter ».
- `docs/01` à `docs/03` : machine, spécifications fonctionnelles, catalogue des objets.
- `docs/04` à `docs/06` : architecture PLC des objets, algorithmes des séquenceurs, tables et interface HMI–PLC. Le simulateur C# les implémente à la lettre.
- `docs/07-hmi-architecture.md` : décisions techniques de la HMI.
- `docs/08-hmi-design-ecrans.md` : design, navigation, écrans, retours visuels en fonctionnement.
- `docs/09-journal-des-decisions.md` : décisions et raisons.
- `docs/10-surveillance-des-rebuts.md` : surveillance des rebuts (règles, réactions, algorithme PLC, écrans).
- `docs/11-interface-usine.md` : interface avec le système de l'usine (première version : contrats, messages, journal de messages, page Communications).
- `docs/12-forcages-et-etats-declares.md` : forçages et états déclarés, page Forçages.
- `docs/14-alarmes.md` : gestion des alarmes et numérotation des codes.
- `docs/ui-reference/Machine_Studio.dc.html` : maquette (référence du langage visuel et de l'usage de l'éditeur de process).

Ne pas modifier les documents 00 à 08 et 10 à 12 sans accord. Si un document est contradictoire, incomplet ou impossible à suivre : le signaler, proposer la correction, et en attendant prendre la décision la plus simple et l'inscrire dans `docs/09-journal-des-decisions.md`.

## Dépôt Git

- Remote : `git@github.com:alimouzin/stdmachine.git`.
- `main` reçoit le premier commit (documents, `CLAUDE.md`, `PROMPT.md`, `.gitignore`). Le développement se fait ensuite sur la branche `develop` ; une branche `feature/<sujet>` par phase si utile.
- Petits commits en français, format Conventional Commits sans portée (`feat: …`, `fix: …`, `test: …`, `docs: …`, `refactor: …`, `chore: …`).
- **Ne jamais pousser** (`git push`) sans demande explicite.
- `.gitignore` .NET dès le premier commit (`bin/`, `obj/`, `.vs/`, `*.user`, `artifacts/`, `TestResults/`).

## Principes à respecter dans le code

- Le PLC (et le simulateur) ne contient aucune logique propre à une machine : tout vient de la configuration et des tables.
- Le catalogue (`catalog/catalog.json`) est la source unique des numéros de types, d'actions, de paramètres, d'erreurs. Jamais de numéro en dur ailleurs.
- Les pages d'objets, le mode manuel, les palettes et les propriétés sont **générés** depuis le catalogue et la configuration, sans code par type quand c'est possible, et jamais de code par station.
- Le simulateur suit `05-plc-sequenceurs.md` à la lettre : un écart est un défaut.
- La HMI ne lit l'état du PLC que dans l'instantané (`PlcSnapshot`) ; elle n'interroge jamais une valeur isolée.

## Conventions C#

- .NET 10, C# 14, `Nullable` activé, `ImplicitUsings`, espaces de noms de fichier, `TreatWarningsAsErrors` dans `Core`, `Simulation`, `Persistence`.
- Identifiants en anglais ; textes affichés en français, dans `Resources/Strings.resx`.
- Instantanés et DTO en `record` immuables. Méthodes asynchrones suffixées `Async`, avec `CancellationToken`.
- MVVM avec CommunityToolkit.Mvvm ; pas de logique métier dans le code-behind.
- Formatage des nombres, unités et durées affichés par un seul utilitaire (`DisplayFormat`) : virgule décimale, signe « − », espace fine avant l'unité.
- Paquets NuGet autorisés : ceux de `docs/07-hmi-architecture.md` § 1. Tout autre paquet : le justifier et demander.

## Conventions d'interface

- Couleurs, tailles, rayons, durées : uniquement les clés de `Theme/Tokens.xaml` (`docs/08` § 2). Aucune valeur en dur dans une vue.
- Composants réutilisables (`docs/08` § 3) ; une page assemble des composants, elle n'en invente pas.
- Tout est tactile : cibles de 48 px minimum, commandes machine 64 px, pas de survol, pas de clic droit, pas de double toucher.
- Glisser-déposer uniquement par `DragDropService` ; saisie uniquement par le pavé numérique et le clavier intégrés.
- Animations par `Storyboard` et états visuels, cadencées par les jetons de mouvement ; clignotements par `BlinkClock`.
- Une interface qui ressemble à un formulaire WPF par défaut (boutons gris, `DataGrid` brut, `MessageBox`) est une erreur à corriger.

## Vérifications à chaque étape

1. `dotnet build` sans avertissement dans les projets concernés.
2. `dotnet test` vert.
3. Après toute modification d'interface : `StdMachine.Hmi.exe --screenshot-all artifacts/screens` puis **regarder les images** produites et les comparer au design (`docs/08`) et, pour l'éditeur de process, à la maquette. Corriger les écarts visibles avant de rendre la main.
4. Un compte rendu court : ce qui est fait, ce qui reste, les écarts connus, les décisions prises (aussi inscrites dans le journal).

## Hors périmètre de la première étape

- Code PLC ST et client ADS : seulement quand ce sera demandé (phase 9 de `PROMPT.md`).
- Statistiques de production : page vide « À définir ».
- Interface usine : seulement les contrats, le modèle des messages, le canal Journal de messages et la page Communications ; MQTT, outbox, OF entrants et OPC UA viendront plus tard.
