# Forçages et états déclarés

Ce document définit la page **Forçages** : déclarer un état de la machine (maintenance, attente, arrêt planifié, essais…), retirer une station ou un mover de la production pour une intervention pendant que le reste produit, et, en simulation, forcer des états et des défauts pour tester des comportements. Les termes suivent `00-glossaire.md`.

## 1. Principes

1. **On ne force jamais le séquenceur dans un état** : aucun bouton ne met la machine ou une station « en Execute » ou « en Idle » sans passer par les commandes PackML. Un forçage agit autour du séquenceur : il retire un élément du routage, bloque un démarrage, déclare une situation, ou (en simulation seulement) provoque un événement.
2. **On ne contourne jamais une sécurité** : aucun forçage n'agit sur les circuits de sécurité ni sur les verrouillages. Une intervention physique sur une station pendant que la machine produit suppose que la station a sa propre zone de sécurité (porte, consignation) : le logiciel ne la remplace pas.
3. **Toujours visible** : tout forçage actif apparaît dans la barre du haut (puce « Forçages : n »), sur la page Forçages, sur l'élément forcé (tuile, en-tête de station, mover), et dans les données remontées à l'usine.
4. **Toujours tracé** : qui, quand, quoi, pourquoi, jusqu'à quand ; journalisé et publié à l'usine.
5. **Fin explicite ou programmée** : un forçage dure jusqu'à ce qu'on le lève, ou jusqu'à une échéance choisie à sa pose (rappel 5 minutes avant).
6. **Persistant** : un forçage survit à un redémarrage de la HMI et du PLC.

## 2. État déclaré de la machine

L'opérateur ou le régleur déclare **ce que fait la machine du point de vue de l'organisation**, indépendamment de son état PackML. C'est l'information dont l'usine a besoin pour classer les temps (TRS) et dont l'atelier a besoin pour savoir s'il peut démarrer.

| État déclaré | Exemple | Démarrage en Production | Balise | Catégorie de temps (TRS) |
| --- | --- | --- | --- | --- |
| Aucun (normal) | — | Permis | Selon l'état PackML | Selon l'état PackML |
| Arrêt planifié | Pause, réunion, changement d'équipe | Permis (lève la déclaration) | — | Arrêt planifié |
| Changement de série | Changement d'OF, de référence, d'outillage | Permis (lève la déclaration) | — | Arrêt planifié |
| Attente amont | Manque de pièces ou de matière | Permis | Bleu clignotant | Arrêt non planifié externe |
| Attente aval | Sortie saturée | Permis | Bleu clignotant | Arrêt non planifié externe |
| Maintenance planifiée | Maintenance préventive | **Bloqué** | Bleu fixe | Arrêt planifié |
| Maintenance corrective | Panne, réparation | **Bloqué** | Bleu fixe | Arrêt non planifié |
| Essais et mise au point | Réglage, essais de process | Bloqué en Production ; permis en Maintenance | Bleu fixe | Essais |
| Nettoyage | Nettoyage de la machine | **Bloqué** | Bleu fixe | Arrêt planifié |

- Pose : choisir l'état (grands boutons), un motif (texte court facultatif), une échéance facultative (30 min, 1 h, 2 h, fin d'équipe, date et heure).
- « Démarrage bloqué » : la commande Démarrer de la machine est refusée avec la raison (« Maintenance corrective déclarée par A. Martin à 09:12 ») ; Abort, Arrêter, Pause restent toujours permis. Le mode manuel et le pas à pas restent disponibles en Maintenance.
- Un seul état déclaré à la fois ; en poser un nouveau termine le précédent.
- **Proposition automatique** : quand la machine reste Stopped ou Held plus de 2 minutes (réglable) sans alarme cause et sans état déclaré, la HMI affiche un bandeau « Qualifier cet arrêt » avec les mêmes boutons ; le choix pose l'état déclaré à partir du début de l'arrêt. Le bandeau peut être ignoré ; l'arrêt reste alors « non qualifié ».
- Le PLC reçoit l'état déclaré (code) et le drapeau « démarrage bloqué » : il refuse lui-même la commande Start (une commande venue d'ailleurs que la HMI est donc aussi bloquée) et publie l'état déclaré dans `GVL_Plant`.

## 3. Station hors production

Retirer une station de la production pour intervenir dessus pendant que les autres stations continuent.

- **Pose** : sur une station, « Mettre hors production » avec un motif (maintenance, réglage, essai, panne, autre) et une échéance facultative. Avant de confirmer, la HMI montre l'effet : les fonctions de la station et les stations qui les assureront encore (« Pressage pignon : Pressage 02 ; Sertissage : Pressage 02 »), ou l'avertissement qu'une fonction n'aura plus de station (« Contrôle n'aura plus aucune station : les movers attendront ») ; confirmation par appui maintenu.
- **Effet** : la station termine son opération en cours, passe Held et sort du routage de toutes ses fonctions (même mécanisme que l'arrêt qualité, `10-surveillance-des-rebuts.md`). Les movers qui l'attendaient en file sont réaffectés.
- **Intervention** : une station hors production peut être arrêtée (Stop de la station) et passée **seule** en mode Maintenance, même si la machine produit : mode manuel, pas à pas, apprentissage des positions deviennent possibles sur cette station. C'est la seule exception à la règle « le mode est celui de la machine ».
- **Retour** : « Remettre en production » : la station repasse en mode Production si elle était en Maintenance (elle doit être Stopped pour changer de mode), puis Réarmer et Démarrer ; la HMI enchaîne ces commandes pour l'utilisateur (trois étapes visibles, chacune confirmée par l'état PLC). La station revient dans le routage.
- Si toutes les stations d'une fonction utilisée sont hors production, la machine ne passe pas en Aborted : les movers qui ont besoin de la fonction attendent en tampon, et la balise passe en bleu clignotant.

## 4. Mover hors service

- « Mettre hors service » un mover avec un motif : il termine son trajet ; ses pièces suivent leur process jusqu'à leur sortie ou leur rebut ; vide, il va en parcage et n'est plus utilisé (même mécanisme que la réaction qualité « mover hors service »).
- Option « Immédiatement » (régleur) : le mover va en parcage dès la fin de son opération en cours ; ses pièces passent « à vider » et l'opérateur confirme leur retrait.
- « Remettre en service » : le mover reprend à l'étape 1 du process général.

## 5. Forçages d'essai (simulation)

Pour tester le comportement de la machine, de la HMI et du système de l'usine, sans attendre que la situation se produise. **Disponibles seulement quand la HMI est connectée au simulateur** ; sur une vraie machine, l'onglet n'apparaît pas.

| Forçage | Effet |
| --- | --- |
| État d'une station | Amener une station dans un état PackML choisi (y compris Aborted avec une alarme de test) |
| Défaut d'objet | Mettre un objet en erreur avec un code et un sous-code choisis, une fois ou à chaque action |
| Résultat d'action | Imposer le résultat ou le jugement des N prochaines actions d'un objet (par exemple 5 pièces mauvaises au palpeur) |
| Entrée | Forcer la valeur d'une entrée simulée |
| Circuit de sécurité | Ouvrir ou fermer un circuit simulé (machine ou station) |
| Système planaire | Collision, mover non détecté |
| Interface usine | Couper la connexion d'un canal, pour tester la remontée différée |
| Échelle de temps | Accélérer ou ralentir la simulation |

Ces forçages passent par `ISimulatorControl` (`06-tables-et-interface-hmi-plc.md` § 7). Ils portent la mention « Essai » dans le journal, les alarmes et les messages vers l'usine.

Sur une vraie machine, le seul forçage de signal reste celui d'une entrée d'un **objet en simulation** (mode Maintenance), déjà prévu dans le mode manuel.

## 6. Droits

| Droit | Code | Niveau par défaut |
| --- | --- | --- |
| Déclarer un état de la machine, qualifier un arrêt | `force.declare` | 0 |
| Déclarer un état qui bloque le démarrage (maintenance, essais, nettoyage) | `force.declareBlocking` | 1 |
| Mettre une station hors production, la remettre en production | `force.station` | 1 |
| Mettre un mover hors service, le remettre en service | `force.mover` | 1 |
| Forçages d'essai en simulation | `force.test` | 2 |

Lever un forçage demande le même droit que le poser.

## 7. PLC et interface

- `ST_MachineForce` (dans `GVL_Hmi`, écrit par commande à handshake `stForceCmd`) : état déclaré (code), démarrage bloqué, échéance ; par station : hors production, motif (code) ; par mover : hors service, immédiat, motif (code).
- Séquenceur général : une station hors production est traitée comme une station en arrêt qualité pour le routage (`05-plc-sequenceurs.md` § 5.4) ; un mover hors service comme un mover verrouillé par la qualité ; la commande Start machine est refusée si le démarrage est bloqué.
- Séquenceur de station : la commande de changement de mode est acceptée pour une station hors production et Stopped, même si la machine produit.
- `GVL_Plant` publie l'état déclaré, les stations hors production et les movers hors service ; la passerelle usine publie chaque pose et chaque levée (`events/force`).
- Échéances et rappels : gérés par la HMI, qui lève le forçage à l'échéance (le PLC garde le dernier état si la HMI est arrêtée).

## 8. Page Forçages

Rail de navigation, groupe Exploitation, icône `engineering`.

```
┌─ État déclaré de la machine ─────────────────────────────────────────────────────────────────────────┐
│  ● Maintenance corrective · démarrage bloqué · depuis 09:12 (A. Martin) · jusqu'à 11:00    [Lever]     │
│  [Arrêt planifié] [Changement de série] [Attente amont] [Attente aval]                                 │
│  [Maintenance planifiée] [Maintenance corrective] [Essais] [Nettoyage]                                 │
├─ Stations ───────────────────────────────────────────────┬─ Movers ──────────────────────────────────┤
│ Chargement      ● Execute                [Hors production]│  ┌1┐ ┌2┐ ┌3┐ ┌4┐ ┌5┐ ┌6┐                  │
│ Pressage 01     ⛭ Hors production · Réglage · 09:40      │  └─┘ └─┘ └✕┘ └─┘ └─┘ └─┘                  │
│                 Mode Maintenance       [Remettre en prod.]│  Mover 3 hors service · nid abîmé        │
│ Pressage 02     ● Execute (assure seule Pressage pignon   │  [Remettre en service]                    │
│                  et Sertissage)          [Hors production]│                                           │
├─ Forçages actifs (3) ────────────────────────────────────┴───────────────────────────────────────────┤
│ Machine · Maintenance corrective · 09:12 · A. Martin · jusqu'à 11:00                         [Lever] │
│ Pressage 01 · Hors production · Réglage · 09:40 · A. Martin                                  [Lever] │
│ Mover 3 · Hors service · Nid abîmé · 08:55 · B. Rossi                                        [Lever] │
├─ Essais (simulateur seulement) ──────────────────────────────────────────────────────────────────────┤
│ Station [▾] État [▾] [Appliquer] · Objet [▾] Défaut [▾] [Injecter] · Résultats [▾] × N [Imposer] …    │
└──────────────────────────────────────────────────────────────────────────────────────────────────────┘
```

- Boutons d'état déclaré : 2 rangées de 4, 72 px de haut, icône + libellé ; ceux qui bloquent le démarrage portent un cadenas ; l'état actif est plein, couleur `Maint`.
- Poser ou lever : feuille avec motif et échéance, confirmation par appui maintenu pour tout ce qui bloque ou retire un élément.
- Barre du haut : puce `Maint` « Forçages : 3 » toujours visible tant qu'un forçage est actif ; la toucher ouvre cette page.
- Page Machine : la tuile d'une station hors production porte un macaron bleu « Hors production » avec l'icône `engineering` ; un mover hors service est gris avec une clé ; l'état déclaré s'affiche sous le badge d'état de la machine.

## 9. Tests

- État déclaré bloquant : Start refusé par le PLC avec la raison ; Abort et Stop toujours acceptés ; levée à l'échéance.
- Qualification proposée après 2 minutes d'arrêt sans cause, et datée du début de l'arrêt.
- Station hors production pendant la production : l'opération en cours se termine, les movers en file sont réaffectés, la fonction est assurée par l'autre station ; passage de la station seule en Maintenance, mode manuel possible ; remise en production en trois étapes.
- Toutes les stations d'une fonction hors production : pas d'Aborted, movers en tampon, balise bleu clignotant.
- Mover hors service normal et immédiat.
- Onglet Essais absent avec un vrai PLC.
- Persistance des forçages après redémarrage de la HMI et du simulateur.
