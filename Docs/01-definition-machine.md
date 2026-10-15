# Définition de la machine standard

## Contexte et objectifs

L'objectif est de créer une machine standard dont le logiciel soit le plus flexible possible, parce que la customisation de la machine est un besoin central.

- Deux parties logicielles : une HMI en C# et un PLC Beckhoff programmé en ST.
- Une architecture la plus basique possible.
- Toute la séquence est définie dans la HMI, puis transmise au PLC.
- À tout moment, on doit savoir où se trouve chaque pièce et dans quel état elle est.

## Description de la machine

Un Beckhoff Planar (ou équivalent) est le centre de la machine. Autour de lui se trouvent trois types de stations, chacun en un ou plusieurs exemplaires.

| Type de station | Rôle | Nombre |
| --- | --- | --- |
| Entrée | Introduit les pièces dans la machine | une ou plusieurs |
| Travail | Réalise des opérations sur les pièces | une ou plusieurs |
| Sortie | Évacue les pièces de la machine | une ou plusieurs |

Chaque mover peut contenir une ou plusieurs pièces. Chaque pièce, et chaque opération réalisée dessus, doit être suivie. Les stations de même fonction peuvent être regroupées (voir les spécifications logicielles).

**Position** : une position est toujours définie par une structure X, Y, Z, C (rotation Rz) et un frame (repère de référence).

## Fonctions requises

En plus du suivi des pièces, la machine doit pouvoir :

- **Gérer des OF** (ordres de fabrication) : les OF sont locaux pour le moment ; prévoir un connecteur pour les récupérer d'un système externe plus tard.
- **Gérer des niveaux d'utilisateurs** : les niveaux sont définissables selon le besoin. Un niveau admin définit le niveau requis pour chaque type d'action.
- **Afficher les statistiques de production** : TODO, à définir plus tard.

## Suivi des movers et des pièces

À tout moment, la machine doit pouvoir répondre à la question « où est qui, et dans quel état ? ». Le suivi porte sur trois niveaux.

- **Mover** : sa position dans la machine et les pièces qu'il contient (une ou plusieurs).
- **Pièce** : le mover qui la porte et son état courant.
- **Opération** : chaque opération réalisée sur une pièce est enregistrée.

Les états possibles et les données conservées par pièce ne sont pas figés : ils dépendent du process défini par l'ingénieur process.
