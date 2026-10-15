# Projet : standard de machine de production (HMI C# + PLC Beckhoff)

Le but de ce dépôt est de créer un standard pour des machines de production : une machine standard dont le logiciel est le plus flexible possible, car la customisation de chaque machine est importante.

## Dépôt Git

- Remote : `git@github.com:alimouzin/stdmachine.git`
- Branche principale : `main`. Travailler par petits commits en français, format Conventional Commits.
- Ne jamais pousser (`git push`) sans demande explicite.
- Ajouter un `.gitignore` .NET (`bin/`, `obj/`, `.vs/`, `*.user`) dès le premier commit.

## Documents de référence

À lire avant tout travail, ils font foi :

- `docs/01-definition-machine.md` : définition de la machine (stations, movers, position, fonctions requises, suivi des pièces).
- `docs/02-specifications-logicielles.md` : architecture logicielle (tables, séquenceurs, niveaux d'abstraction, modèle d'états PackML réduit, modes manuel et pas à pas).

Ne pas modifier ces documents sans accord : si une décision change, le signaler et proposer la mise à jour.

## Principes d'architecture

- Architecture la plus basique possible. Pas de généralisation prématurée.
- Toute la séquence est définie dans la HMI puis transmise au PLC sous forme de tables. Le PLC exécute, il ne porte pas la logique du process.
- Empilement de niveaux : devices, actions de base, modules standard, opérations de station, process général.
- Une position est toujours une structure X, Y, Z, C (rotation Rz) et un frame.
- Le modèle d'états de chaque séquenceur est le PackML réduit : 8 états, 6 commandes, 2 modes.
- Les actions manuelles et les pages de station sont générées à partir de la déclaration des devices et des modules, sans code par station.
- Les droits d'accès sont des niveaux d'utilisateur définissables ; un niveau admin définit le niveau requis par type d'action.

## Stack et conventions (première étape : HMI seule)

- C#, .NET 8, WPF, MVVM. Pas de bibliothèque d'UI externe pour le moment. Si un paquet NuGet est utile, le justifier avant de l'ajouter.
- L'interface cible est un écran tactile industriel : grands boutons, zones de toucher généreuses, pas de survol nécessaire.
- Identifiants de code en anglais, textes affichés à l'utilisateur en français.
- Le PLC n'existe pas encore : la HMI parle à un `IPlcClient`. La première implémentation est un simulateur. L'implémentation ADS (Beckhoff) viendra plus tard sans changer le reste.
- Pas de code ST dans cette étape.
- Tests unitaires pour la logique (machine à états, droits, génération des actions manuelles).

## Hors périmètre pour le moment

- Statistiques de production : TODO, uniquement une page vide marquée « à définir ».
- Connecteur OF externe : les OF sont locaux, prévoir seulement l'interface (`IOrderSource`).
- Versionnage des tables : non nécessaire, elles ne changent que machine à l'arrêt.
- Synchronisation entre stations : non prévue.

## Façon de travailler

- Commencer par proposer un plan, attendre validation avant d'écrire du code.
- Faire des petits pas qui compilent : `dotnet build` doit passer à chaque étape.
- Quand une décision manque dans les documents, la poser en question plutôt que de la deviner.
