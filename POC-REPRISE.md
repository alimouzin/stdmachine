# PoC HMI : état et reprise (phases 0 et 1)

> **Mise à jour du 2026-10-16 (soir)** : le PoC a été validé sur l'écran réel et les phases 2 à 4 (et une partie des phases 5 et 8) ont avancé en session distante : voir `REPRISE-PHASES-2-A-5.md`.

Dernière mise à jour : 2026-10-16. Branche `develop`, aucun remote, aucun `git push`. Dernier commit : « feat: accès plus visibles aux fonctions personnalisées ».

## Contexte à relire en début de session
1. `CLAUDE.md`, puis `POC-NET48.md` (prime sur .NET 10 pour le PoC), `PROMPT.md`, `docs/README.md`.
2. Ne pas modifier les documents 00 à 08 et 10 à 12 sans accord. Décisions dans `docs/09-journal-des-decisions.md` (D41 à D48 ajoutées pendant le PoC).
3. Commits en français (Conventional Commits), trailer `Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>`.

## Ce qui est fait
- Solution .NET Framework 4.8 (WPF, C# 11, CommunityToolkit.Mvvm 8.2.2), compilée par MSBuild uniquement : `powershell -File tools\build.ps1` (Git Bash abîme les arguments `/t:`). 0 erreur, 0 avertissement.
- Projets : `StdMachine.Core` (modèle, `PlcSnapshot`, `IPlcClient`), `StdMachine.Simulation` (`Demo/` : projet MX-4, scénario de 150 s en boucle, `DemoPlcClient`, `DemoExtras`, `DemoFunctions`), `StdMachine.Hmi` (thème, composants, `DragDropService`, coquille, pages).
- Coquille : rail de 15 entrées (64 px), barre du haut, bandeau d'alarme, surimpressions (pavé numérique, clavier, tiroir, dialogue, toasts, sélecteurs), mode capture.
- 22 pages avec données de démonstration : Machine, Station, Process (niveaux machine et station), Manuel, Alarmes, Qualité, Forçages, Pièces, OF, Studio machine, Studio station, Fonction personnalisée, Déploiement, Utilisateurs, Statistiques (vide), Communications, Système, Essai tactile, Composants (2 pages).
- Réagissent vraiment : éditeur de process (palette, insertion avant ou après, branches, déplacement, refus expliqués, panneau Étape, annuler et refaire), glisser une station sur une fonction (Studio machine), dépôt depuis la bibliothèque sur la vue de dessus (Studio station), éditeur de fonction personnalisée, commandes machine et station, forçages, OF, libération qualité, acquittement des alarmes.
- Fonctions personnalisées (D48) : Studio station, onglet « Fonctions » (aussi ligne d'arborescence et boutons des propriétés du robot) ; page `FunctionEditor:<station>:<id>` ; palette de l'éditeur de process avec crayon ✎. Exemple « Prendre pièce douce » dans Pressage 01 et 02.

## Commandes utiles
- Lancer : `src\StdMachine.Hmi\bin\Debug\net48\StdMachine.Hmi.exe` (`--windowed`, `--page <clé>`, `--topmost` pour les essais).
- Une page : `StdMachine.Hmi.exe --screenshot <clé> <fichier.png> [--at <s>]`. Clés avec arguments : `Station:2`, `ProcessEditor:2:presser`, `ProcessEditor:General`, `StationConfig:2:fn`, `FunctionEditor:2:x`.
- Toutes les pages : `powershell -File tools\capture.ps1` (efface `artifacts\screens`, environ 2 min, images `demo-0|30|60|90`).
- Essais d'entrée réels : `tools\test-dnd.ps1`, `test-touch.ps1`, `test-editor.ps1`, `test-function.ps1` (résultats dans `artifacts\dnd`, `touch`, `editor`, `function`). Sécurité D47 : abandon si le pointeur n'est pas au-dessus de la fenêtre de l'application.
- Icônes : ajouter le nom dans `tools\IconTableGen\icons.txt` puis `tools\gen-icons.ps1`.

## Pièges rencontrés
- Dans ce shell, les `\a` et `\f` des chemins Windows sont interprétés dans les `sed` : écrire les chemins avec l'outil Edit ou PowerShell.
- Variables PowerShell insensibles à la casse (`$h` écrasé par `$H`) ; le handle de fenêtre s'appelle `$hwnd`.
- Fichiers en CRLF : les `sed` simples marchent, les regex multilignes non.
- Les pages à `x:Class` ont toutes un code-behind minimal (`InitializeComponent()`).
- Un nouveau ViewModel de page doit avoir : son `DataTemplate` dans `App.xaml` et son `nav.Register` dans `Services/PageRegistry.cs`.

## Écarts connus (D45, D46)
Chronogramme réduit à un texte, pas de mini-carte ni de pincement, validation de démonstration à 6 règles, tout code PIN à 4 chiffres accepté, textes de pages en dur (seul le vocabulaire fixe est dans `Strings.resx`), jog des axes sans effet, `IAsyncDisposable` remplacé par `Task DisposeAsync()`, fonctions personnalisées en suite linéaire d'actions de base (pas de blocs).

## Prochaine étape
Attendre la validation explicite de la maquette par l'utilisateur sur son écran réel (fluidité des animations, ergonomie tactile, décision éventuelle WebView2 pour l'éditeur), puis phase 2 (domaine, catalogue JSON, validation complète) selon `PROMPT.md`. Pas de mode sombre (refusé). Les captures de toutes les pages n'ont pas été régénérées depuis l'ajout de la page Fonction personnalisée.
