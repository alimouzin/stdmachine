# Catalogue des objets et des modules

Ce document est la spécification du **catalogue** : tous les types d'objets et de modules, avec leurs paramètres, signaux, actions, résultats, états affichés, erreurs, état sûr et comportement simulé. Il est la source du fichier `catalog/catalog.json` de la HMI (format dans `07-hmi-architecture.md`) et des constantes du PLC (`04-plc-architecture-objets.md`). Toute numérotation ici est définitive : elle est partagée par la HMI et le PLC.

## 1. Conventions

### 1.1 Numérotation

- **Type** : numéro unique (colonne « Type »). Les numéros 1 à 99 sont des objets, 100 à 149 des modules.
- **Paramètres** : `P1` à `P24`, nombres réels rangés dans `aParam[1..24]` de la configuration de l'objet. Booléens et listes sont des réels (0/1, index de liste à partir de 0).
- **Signaux** : `S1` à `S8`, références vers l'image E/S (`DI`, `DO`, `AI`, `AO`).
- **Matériel** : `D`, numéro d'élément de pool matériel (axe NC, esclave EtherCAT, connexion réseau).
- **Vitesses** : `V1` à `V8` en % de la Vmax (axes seulement). V1 Lente, V2 Rapide, V3 à V8 personnalisées.
- **Actions** : numéros 1 à 31 par type. Paramètres d'action `a1` à `a8`, résultats `r1` à `r8`.
- **État affiché** : jusqu'à 4 valeurs `E1` à `E4` publiées en continu par l'objet (position actuelle, valeur lue…).

### 1.2 Types de valeurs

| Type | Contenu | Saisie HMI |
| --- | --- | --- |
| `Bool` | 0 ou 1 | Interrupteur |
| `Int` | Entier | Pavé numérique |
| `Real` | Réel avec unité | Pavé numérique, bornes affichées |
| `Percent` | 0 à 100 % | Curseur + pavé |
| `Duration` | Millisecondes | Pavé numérique, affichage en ms ou s |
| `Enum` | Index dans une liste de libellés | Liste déroulante tactile |
| `Position` | X, Y, Z, C, frame | Sélecteur de position nommée, ou variable de station de type position |
| `Speed` | N° de vitesse (1 à 8) ou % direct | Liste des vitesses de l'axe + « % personnalisé » |
| `Text` | Texte (80 caractères) | Clavier |
| `Recipe` | N° de recette ou de programme d'appareil | Pavé numérique, avec libellé si connu |

Un paramètre d'action peut toujours recevoir une constante, une variable de station, une donnée de pièce ou un paramètre d'opération, si le type correspond.

### 1.3 Commun à tous les objets

**Configuration commune** (hors `P1..P24`) : nom, simulation (Bool), désactivé (Bool), override (Percent, seulement si l'objet a des actions de mouvement), timeout par action (Duration, 0 = défaut du catalogue), verrouillages.

**États d'objet** : 0 Non initialisé, 1 Prêt, 2 En cours, 3 En erreur, 4 Désactivé.

**Statut d'action** : 0 Inactif, 1 En cours, 2 Terminée, 3 En erreur.

**Erreurs communes** (codes 1 à 99, valables pour tous les types) :

| Code | Texte | Sous-code |
| --- | --- | --- |
| 1 | Timeout de l'action | N° d'action |
| 2 | Verrouillage actif | N° de verrouillage |
| 3 | Objet non prêt | État de l'objet |
| 4 | Action inconnue pour ce type | N° d'action |
| 5 | Paramètre d'action invalide | N° de paramètre |
| 6 | Action interrompue par Abort | N° d'action |
| 7 | Objet désactivé | — |
| 8 | Communication perdue | Code du driver |
| 9 | Objet non initialisé | — |
| 10 | Matériel non raccordé | — |
| 11 | Action réservée au mode manuel | N° d'action |

Les codes propres à un type commencent à 100.

## 2. Liste des types

| Type | Code | Nom | Catégorie | Icône (Material Symbols) | Mouvement |
| --- | --- | --- | --- | --- | --- |
| 1 | `DigitalInput` | Entrée numérique | Entrées/sorties | `login` | non |
| 2 | `DigitalOutput` | Sortie numérique | Entrées/sorties | `logout` | non |
| 3 | `AnalogInput` | Entrée analogique | Entrées/sorties | `monitoring` | non |
| 4 | `AnalogOutput` | Sortie analogique | Entrées/sorties | `tune` | non |
| 10 | `Cylinder` | Vérin | Mouvement | `swap_vert` | oui |
| 11 | `LinearAxis` | Axe linéaire | Mouvement | `straighten` | oui |
| 12 | `RotaryAxis` | Axe rotatif | Mouvement | `rotate_right` | oui |
| 13 | `PlanarSystem` | Système planaire | Mouvement | `grid_view` | non |
| 14 | `PlanarMover` | Mover | Mouvement | `crop_square` | oui |
| 15 | `ExternalRobot` | Robot externe | Mouvement | `smart_toy` | oui |
| 20 | `VacuumBlow` | Aspiration et soufflage | Préhension | `air` | non |
| 21 | `Gripper` | Pince | Préhension | `back_hand` | non |
| 30 | `KeyenceIV` | Vision Keyence IV | Capteurs et mesure | `photo_camera` | non |
| 31 | `KeyenceGT2` | Palpeur Keyence GT2 | Capteurs et mesure | `height` | non |
| 32 | `AsyrilEyePlus` | Vision Asyril Eye+ | Capteurs et mesure | `center_focus_strong` | non |
| 33 | `Scale` | Pesage | Capteurs et mesure | `scale` | non |
| 34 | `CodeReader` | Lecteur de code | Capteurs et mesure | `qr_code_scanner` | non |
| 35 | `Light` | Éclairage | Capteurs et mesure | `lightbulb` | non |
| 40 | `Press` | Presse | Process | `compress` | oui |
| 41 | `Screwdriver` | Visseuse | Process | `build` | non |
| 42 | `Dispenser` | Distributeur | Process | `water_drop` | non |
| 43 | `Marker` | Marquage | Process | `draw` | non |
| 44 | `VibratoryFeeder` | Chargeur vibrant | Process | `vibration` | non |
| 45 | `Magazine` | Palettiseur ou magasin | Process | `pallet` | non |
| 50 | `Beacon` | Balise | Machine | `emergency_heat` | non |
| 51 | `Safety` | Sécurités | Machine | `shield` | non |
| 52 | `Timer` | Temporisation | Logique | `timer` | non |
| 53 | `Counter` | Compteur | Logique | `exposure_plus_1` | non |
| 100 | `Group` | Groupe | Modules | `folder` | — |
| 101 | `CartesianRobot` | Robot cartésien XYZC | Modules | `precision_manufacturing` | — |

« Mouvement : oui » signifie que l'objet a au moins une action de mouvement (ralentie par l'override, timeout recalculé).

Les types 41, 42, 43 et 15 ont une interface matérielle encore à définir : leur fiche est complète côté HMI et simulateur, leur implémentation PLC réelle est reportée.

## 3. Entrées et sorties

### 3.1 Entrée numérique — type 1

Signaux : `S1` DI, obligatoire.

| Param. | Nom | Type | Défaut | Bornes |
| --- | --- | --- | --- | --- |
| P1 | Inversion | Bool | 0 | — |
| P2 | Filtrage anti-rebond | Duration | 10 | 0 à 1000 |

État affiché : `E1` valeur filtrée (0/1).

| N° | Action | Paramètres | Résultats | Timeout défaut |
| --- | --- | --- | --- | --- |
| 1 | Lire | — | r1 Valeur (Bool) | — (immédiate) |
| 2 | Attendre actif | — | — | 5 000 ms |
| 3 | Attendre inactif | — | — | 5 000 ms |

Erreurs : communes seulement. État sûr : sans objet. Simulation : la valeur simulée se force dans la page manuelle ; « Attendre » se termine après 300 ms en simulation si personne ne force.

### 3.2 Sortie numérique — type 2

Signaux : `S1` DO, obligatoire.

| Param. | Nom | Type | Défaut | Bornes |
| --- | --- | --- | --- | --- |
| P1 | Inversion | Bool | 0 | — |
| P2 | Durée d'impulsion par défaut | Duration | 200 | 1 à 60 000 |
| P3 | État sûr | Enum (Faux, Vrai, Maintien) | 0 | — |

État affiché : `E1` valeur commandée.

| N° | Action | Paramètres | Résultats | Timeout défaut |
| --- | --- | --- | --- | --- |
| 1 | Activer | — | — | immédiate |
| 2 | Désactiver | — | — | immédiate |
| 3 | Impulsion | a1 Durée (Duration, défaut P2) | — | durée + 1 000 ms |

État sûr sur Abort : P3.

### 3.3 Entrée analogique — type 3

Signaux : `S1` AI, obligatoire.

| Param. | Nom | Type | Défaut | Bornes |
| --- | --- | --- | --- | --- |
| P1 | Valeur brute min | Real | 0 | — |
| P2 | Valeur brute max | Real | 32767 | — |
| P3 | Valeur min en unité | Real | 0 | — |
| P4 | Valeur max en unité | Real | 10 | — |
| P5 | Unité | Enum (V, mA, bar, kPa, N, mm, °C, %) | 0 | — |
| P6 | Filtrage (moyenne glissante) | Int (échantillons) | 1 | 1 à 100 |
| P7 | Seuil d'alarme bas | Real | −1e9 | — |
| P8 | Seuil d'alarme haut | Real | 1e9 | — |

État affiché : `E1` valeur en unité.

| N° | Action | Paramètres | Résultats | Timeout défaut |
| --- | --- | --- | --- | --- |
| 1 | Lire | — | r1 Valeur (Real) | immédiate |
| 2 | Attendre seuil | a1 Seuil (Real), a2 Sens (Enum : au-dessus, en dessous) | r1 Valeur | 5 000 ms |
| 3 | Vérifier plage | a1 Min (Real), a2 Max (Real) | r1 Valeur, r2 Jugement | immédiate |

Erreurs : 100 Valeur sous le seuil d'alarme bas, 101 Valeur au-dessus du seuil d'alarme haut (avertissements, surveillés en continu).

### 3.4 Sortie analogique — type 4

Signaux : `S1` AO, obligatoire.

| Param. | Nom | Type | Défaut |
| --- | --- | --- | --- |
| P1 à P5 | Échelle et unité | comme l'entrée analogique | — |
| P6 | Limite min | Real | 0 |
| P7 | Limite max | Real | 10 |
| P8 | Valeur sûre | Real | 0 |
| P9 | Variation max | Real (unité/s) | 0 = illimitée |

État affiché : `E1` valeur commandée.

| N° | Action | Paramètres | Résultats | Timeout défaut |
| --- | --- | --- | --- | --- |
| 1 | Écrire | a1 Valeur | — | immédiate |
| 2 | Écrire avec rampe | a1 Valeur, a2 Durée | — | durée + 1 000 ms |
| 3 | Valeur sûre | — | — | immédiate |

État sûr : P8.

## 4. Mouvement

### 4.1 Vérin — type 10

Signaux : `S1` DO commande travail (obligatoire), `S2` DO commande repos (bistable seulement), `S3` DI fin de course travail (optionnel), `S4` DI fin de course repos (optionnel).

| Param. | Nom | Type | Défaut | Bornes |
| --- | --- | --- | --- | --- |
| P1 | Commande | Enum (Monostable, Bistable) | 0 | — |
| P2 | Temps de stabilisation | Duration | 50 | 0 à 5 000 |
| P3 | Temps de déplacement sans capteur | Duration | 500 | 10 à 30 000 |
| P4 | Position sûre sur Abort | Enum (Repos, Travail, Maintien) | 0 | — |
| P5 | Ordre d'initialisation | Int | 1 | 0 à 9 |

Timeouts par défaut : Move travail et Move repos 2 000 ms (P3 + 500 ms si pas de capteur).

État affiché : `E1` position (0 Inconnue, 1 Repos, 2 Travail, 3 Intermédiaire, 4 Incohérente).

| N° | Action | Paramètres | Résultats | Mouvement |
| --- | --- | --- | --- | --- |
| 1 | Move travail | — | — | oui |
| 2 | Move repos | — | — | oui |
| 3 | Lire position | — | r1 Position (Enum) | non |

Fin de mouvement : capteur atteint puis P2 écoulé ; sans capteur, P3 écoulé.

| Code | Erreur |
| --- | --- |
| 100 | Les deux capteurs actifs |
| 101 | Perte du capteur de la position atteinte hors mouvement |
| 102 | Capteur de départ toujours actif après 500 ms (vérin bloqué) |

Override : pas de vitesse pilotée ; à 0 % un nouveau mouvement attend ; le timeout est recalculé. Simulation : déplacement en P3 (ou 300 ms si P3 = 0), capteurs simulés.

### 4.2 Axe linéaire — type 11, axe rotatif — type 12

Matériel : `D` élément du pool d'axes NC (obligatoire). Vitesses `V1..V8`.

| Param. | Nom | Type | Défaut linéaire | Défaut rotatif |
| --- | --- | --- | --- | --- |
| P1 | Vmax | Real (mm/s ou °/s) | 500 | 720 |
| P2 | Accélération | Real (unité/s²) | 2 000 | 3 600 |
| P3 | Décélération | Real (unité/s²) | 2 000 | 3 600 |
| P4 | Fenêtre de position atteinte | Real | 0,02 | 0,05 |
| P5 | Course min | Real | 0 | −180 |
| P6 | Course max | Real | 300 | 180 |
| P7 | Limites logicielles actives | Bool | 1 | 0 (modulo 360°) |
| P8 | Position de homing | Real | 0 | 0 |
| P9 | Méthode de référencement | Enum (Capteur de référence, Butée, Index codeur, Position actuelle) | 0 | 0 |
| P10 | Ordre de référencement | Int | 1 | 3 |
| P11 | Après Abort | Enum (Alimentation maintenue, Alimentation coupée) | 0 | 0 |
| P12 | Override manuel | Percent | 50 | 50 |
| P13 | Composante | Enum (X, Y, Z) / fixe C | 0 | C |
| P14 | Direction par défaut | — / Enum (Plus court, Positif, Négatif) | — | 0 |

Vitesses par défaut : V1 Lente 10 %, V2 Rapide 80 %.

État affiché : `E1` position actuelle, `E2` vitesse actuelle, `E3` alimenté (0/1), `E4` référencé (0/1).

| N° | Action | Paramètres | Résultats | Mouvement | Timeout défaut |
| --- | --- | --- | --- | --- | --- |
| 1 | Power on | — | — | non | 3 000 ms |
| 2 | Power off | — | — | non | 3 000 ms |
| 3 | Init | — | — | oui | 30 000 ms |
| 4 | Move absolu | a1 Position (Position ou Real), a2 Vitesse (Speed), a3 Accélération % (Percent, défaut 100), a4 Direction (rotatif) | r1 Position atteinte | oui | calculé* |
| 5 | Move relatif | a1 Distance (Real), a2 Vitesse | r1 Position atteinte | oui | calculé* |
| 6 | Stop | — | — | non | 3 000 ms |
| 7 | Acquitter erreur | — | — | non | 3 000 ms |
| 8 | Lire position | — | r1 Position | non | immédiate |
| 9 | Jog + | a1 Vitesse (défaut V1) | — | oui, manuel seulement | — |
| 10 | Jog − | a1 Vitesse (défaut V1) | — | oui, manuel seulement | — |

\* Timeout calculé quand l'étape ne fixe pas de timeout : 2 × (distance ÷ vitesse nominale + vitesse ÷ accélération) + 1 000 ms, à override 100 %.

Quand `a1` est une position (X, Y, Z, C, frame), l'axe prend sa composante P13.

| Code | Erreur | Sous-code |
| --- | --- | --- |
| 100 | Axe non référencé | — |
| 101 | Cible hors course | — |
| 102 | Erreur du drive | Code d'erreur NC (fichier de sous-alarmes TwinCAT NC) |
| 103 | Erreur de suivi | — |
| 104 | Perte d'alimentation | — |
| 105 | Axe non alimenté | — |

État sûr sur Abort : arrêt avec la décélération maximale, puis P11. Simulation : profil trapézoïdal (Vmax, accélérations) multiplié par l'override effectif ; Init dure 1,5 s.

### 4.3 Système planaire — type 13 (niveau machine)

Matériel : `D` groupe planaire Tc3 (obligatoire).

| Param. | Nom | Type | Défaut |
| --- | --- | --- | --- |
| P1 | Vitesse par défaut des movers | Real (mm/s) | 1 000 |
| P2 | Accélération par défaut | Real (mm/s²) | 5 000 |
| P3 | Fenêtre de position atteinte | Real (mm) | 0,1 |

État affiché : `E1` système actif (0/1), `E2` nombre de movers détectés.

| N° | Action | Paramètres | Résultats | Timeout défaut |
| --- | --- | --- | --- | --- |
| 1 | Activer le système | — | r1 Movers détectés (Int) | 20 000 ms |
| 2 | Désactiver le système | — | — | 10 000 ms |
| 3 | Acquitter erreur | — | — | 5 000 ms |

Erreurs : 100 Collision détectée, 101 Mover non détecté, 102 Erreur du groupe planaire (sous-code : code Tc3). Ces erreurs sont des défauts machine. État sûr : arrêt contrôlé de tous les movers, maintien en lévitation.

### 4.4 Mover — type 14 (niveau machine, un par mover)

Matériel : `D` n° du mover dans le groupe planaire.

État affiché : `E1` X, `E2` Y, `E3` C, `E4` vitesse.

| N° | Action | Paramètres | Résultats | Mouvement | Timeout défaut |
| --- | --- | --- | --- | --- | --- |
| 1 | Move vers position | a1 Position, a2 Vitesse (Percent de P1 du système) | — | oui | 30 000 ms |
| 2 | Stop | — | — | non | 3 000 ms |
| 3 | Lire position | — | r1 Position | non | immédiate |

Les movers sont commandés par le séquenceur général, pas par les process de station. Le mode manuel les déplace vers une position planaire choisie sur le plan.

### 4.5 Robot externe — type 15 (interface à définir)

Matériel : `D` connexion au contrôleur du robot.

| Param. | Nom | Type | Défaut |
| --- | --- | --- | --- |
| P1 | Frame de base (n° de frame) | Int | 1 |
| P2 à P5 | Outil : décalage X, Y, Z, C | Real | 0 |
| P6 | Vitesse par défaut | Percent | 50 |

| N° | Action | Paramètres | Résultats | Mouvement | Timeout défaut |
| --- | --- | --- | --- | --- | --- |
| 1 | Init | — | — | oui | 60 000 ms |
| 2 | Aller à | a1 Position, a2 Vitesse (Percent) | — | oui | 20 000 ms |
| 3 | Prendre | a1 Position | — | oui | 20 000 ms |
| 4 | Déposer | a1 Position | — | oui | 20 000 ms |
| 5 | Exécuter programme | a1 Programme (Recipe) | r1 Code retour | oui | 60 000 ms |
| 6 | Stop | — | — | non | 3 000 ms |
| 7 | Acquitter erreur | — | — | non | 5 000 ms |

Erreurs : 100 Robot en défaut (sous-code contrôleur), 101 Position hors de portée, 102 Zone interdite. État sûr : arrêt immédiat.

## 5. Préhension

### 5.1 Aspiration et soufflage — type 20

Signaux : `S1` DO aspiration (obligatoire), `S2` DO soufflage (optionnel), `S3` DI vide atteint (pressostat) ou `S4` AI vide (kPa) — l'un des deux.

| Param. | Nom | Type | Défaut |
| --- | --- | --- | --- |
| P1 | Seuil « pièce présente » (capteur analogique) | Real (kPa) | −60 |
| P2 | Seuil « buse vide » (capteur analogique) | Real (kPa) | −52 |
| P3 | Durée de soufflage par défaut | Duration | 50 |
| P4 | Sur Abort | Enum (Tout couper, Maintenir l'aspiration) | 1 |

État affiché : `E1` aspiration (0/1), `E2` soufflage (0/1), `E3` vide (kPa, ou 0/1), `E4` pièce présente (0/1).

| N° | Action | Paramètres | Résultats | Timeout défaut |
| --- | --- | --- | --- | --- |
| 1 | Aspirer | a1 Attendre le vide (Bool, défaut 1) | r1 Pièce présente | 1 000 ms |
| 2 | Souffler | a1 Durée (défaut P3) | — | durée + 500 ms |
| 3 | Couper | — | — | immédiate |
| 4 | Vérifier présence pièce | — | r1 Pièce présente (Bool) | immédiate |

Aspiration et soufflage ne sont jamais actifs ensemble. Erreurs : 100 Vide non atteint (pièce absente ou fuite), 101 Perte de vide pendant l'aspiration (pièce tombée), 102 Seuils incohérents (P1 ≥ P2). Le validateur avertit si l'écart P2 − P1 est inférieur à 10 kPa.

### 5.2 Pince — type 21

Signaux (pneumatique) : `S1` DO fermer, `S2` DO ouvrir (bistable), `S3` DI fermée, `S4` DI ouverte, `S5` DI présence pièce — `S3` à `S5` optionnels. Électrique : `D` interface à définir.

| Param. | Nom | Type | Défaut |
| --- | --- | --- | --- |
| P1 | Type | Enum (Pneumatique monostable, Pneumatique bistable, Électrique) | 0 |
| P2 | Temps de fermeture sans capteur | Duration | 120 |
| P3 | Force de serrage par défaut (électrique) | Real (N) | 20 |
| P4 | Course d'ouverture (électrique) | Real (mm) | 5 |

État affiché : `E1` état (0 Inconnu, 1 Ouverte, 2 Fermée, 3 Fermée sur pièce), `E2` pièce présente.

| N° | Action | Paramètres | Résultats | Timeout défaut |
| --- | --- | --- | --- | --- |
| 1 | Ouvrir | — | — | 1 000 ms |
| 2 | Fermer | — | — | 1 000 ms |
| 3 | Serrer sur pièce | a1 Force (Real, électrique) | r1 Pièce présente | 1 000 ms |
| 4 | Vérifier présence pièce | — | r1 Pièce présente | immédiate |

Erreurs : 100 Pièce absente à la fermeture, 101 Pièce perdue pendant le maintien, 102 Capteurs incohérents. État sûr : maintien (ne s'ouvre pas).

## 6. Capteurs et mesure

### 6.1 Vision Keyence IV — type 30

Signaux (mode signaux) : `S1` DO déclenchement, `S2` DI prêt, `S3` DI résultat OK, `S4` DI résultat NOK. Mode réseau : `D`. Le mode reste à choisir ; le PLC implémente d'abord le mode signaux.

| Param. | Nom | Type | Défaut |
| --- | --- | --- | --- |
| P1 | Mode de communication | Enum (Signaux, Réseau) | 0 |
| P2 | Programme par défaut | Int | 0 |
| P3 | Attente avant lecture du résultat | Duration | 20 |

| N° | Action | Paramètres | Résultats | Timeout défaut |
| --- | --- | --- | --- | --- |
| 1 | Sélectionner programme | a1 Programme (Recipe) | — | 3 000 ms |
| 2 | Déclencher | — | r1 Jugement, r2 Valeur mesurée (si disponible) | 2 000 ms |
| 3 | Lire dernier résultat | — | r1 Jugement, r2 Valeur | immédiate |

Erreurs : 100 Capteur non prêt. Simulation : jugement bon avec une probabilité de 95 % (réglable dans le simulateur).

### 6.2 Palpeur Keyence GT2 — type 31

Signaux : `S1` AI valeur (mode signaux). Mode réseau : `D`.

| Param. | Nom | Type | Défaut |
| --- | --- | --- | --- |
| P1 | Mode de communication | Enum (Signaux, Réseau) | 0 |
| P2 | Valeur de référence | Real (mm) | 0 |
| P3 | Tolérance haute | Real (mm) | 0,02 |
| P4 | Tolérance basse | Real (mm) | −0,02 |
| P5 | Nombre d'échantillons | Int | 5 |
| P6 | Stabilisation avant mesure | Duration | 100 |

État affiché : `E1` valeur courante (mm).

| N° | Action | Paramètres | Résultats | Timeout défaut |
| --- | --- | --- | --- | --- |
| 1 | Mesurer | a1 Échantillons (Int, défaut P5) | r1 Valeur moyenne (mm), r2 Jugement (trop haut, bon, trop bas) | 2 000 ms |
| 2 | Remise à zéro | — | — | 1 000 ms |
| 3 | Lire valeur courante | — | r1 Valeur | immédiate |

Erreurs : 100 Mesure hors plage, 101 Capteur non prêt.

### 6.3 Vision Asyril Eye+ — type 32

Matériel : `D` esclave EtherCAT Eye+ (image process).

| Param. | Nom | Type | Défaut |
| --- | --- | --- | --- |
| P1 | Recette de vision par défaut | Int | 1 |
| P2 | Frame de la vision (n°) | Int | 2 |
| P3 | Timeout de réponse | Duration | 2 000 |

État affiché : `E1` pièces trouvées (dernière recherche), `E2` recette active.

| N° | Action | Paramètres | Résultats | Timeout défaut |
| --- | --- | --- | --- | --- |
| 1 | Init | — | — | 10 000 ms |
| 2 | Sélectionner recette | a1 Recette de vision (Recipe) | — | 5 000 ms |
| 3 | Chercher pièce | — | r1 Pièces trouvées (Int) | 3 000 ms |
| 4 | Obtenir pièce | — | r1 Position de la pièce (Position, frame P2) | 1 000 ms |

Erreurs : 100 Esclave EtherCAT hors ligne, 101 Aucune pièce trouvée, 102 Résultat invalide, 103 Erreur Asyril (sous-code : fichier de sous-alarmes Asyril). Politique d'échec conseillée pour Chercher pièce : refaire 3 fois (avec Vibrer du chargeur entre deux essais, dans le process). Simulation : 0 à 6 pièces, position aléatoire dans le plateau, aucune pièce avec une probabilité de 5 %.

### 6.4 Pesage — type 33

Signaux : `S1` AI (mode analogique). Mode réseau : `D`.

| Param. | Nom | Type | Défaut |
| --- | --- | --- | --- |
| P1 | Unité | Enum (g, mg) | 0 |
| P2 | Tolérance haute | Real | 0 |
| P3 | Tolérance basse | Real | 0 |
| P4 | Valeur nominale | Real | 0 |
| P5 | Nombre d'échantillons | Int | 10 |
| P6 | Temps de stabilisation | Duration | 500 |

| N° | Action | Paramètres | Résultats | Timeout défaut |
| --- | --- | --- | --- | --- |
| 1 | Tarer | — | — | 3 000 ms |
| 2 | Mesurer | a1 Échantillons | r1 Valeur, r2 Jugement | 5 000 ms |
| 3 | Lire valeur courante | — | r1 Valeur, r2 Stable (Bool) | immédiate |

Erreurs : 100 Mesure non stabilisée, 101 Surcharge.

### 6.5 Lecteur de code — type 34

Matériel : `D` (réseau) ; mode à définir. Le simulateur génère des codes « MX4-AAAAMMJJ-nnnnnn ».

| Param. | Nom | Type | Défaut |
| --- | --- | --- | --- |
| P1 | Types de codes | Enum (DataMatrix, QR, Code-barres, Tous) | 0 |
| P2 | Nombre d'essais | Int | 2 |

| N° | Action | Paramètres | Résultats | Timeout défaut |
| --- | --- | --- | --- | --- |
| 1 | Lire code | — | r1 Contenu (Text), r2 Qualité (Real, %) | 2 000 ms |
| 2 | Vérifier code | a1 Motif attendu (Text, `*` et `?` autorisés) | r1 Contenu, r2 Jugement | 2 000 ms |

Erreurs : 100 Aucun code lu, 101 Code non conforme au motif.

### 6.6 Éclairage — type 35

Signaux : `S1` DO commande, `S2` AO intensité (optionnel), `S3` DO synchronisation caméra (optionnel).

| Param. | Nom | Type | Défaut |
| --- | --- | --- | --- |
| P1 | Type | Enum (Continu, Flash) | 0 |
| P2 | Intensité par défaut | Percent | 100 |
| P3 | Durée de flash par défaut | Duration | 5 |

| N° | Action | Paramètres | Résultats | Timeout défaut |
| --- | --- | --- | --- | --- |
| 1 | Allumer | a1 Intensité (Percent, défaut P2) | — | immédiate |
| 2 | Éteindre | — | — | immédiate |
| 3 | Régler intensité | a1 Intensité | — | immédiate |
| 4 | Flasher | a1 Durée, a2 Intensité | — | durée + 200 ms |

État sûr : éteint.

## 7. Process

### 7.1 Presse — type 40

Matériel : `D` axe NC de la presse. Signaux : `S1` AI capteur de force (N).

| Param. | Nom | Type | Défaut |
| --- | --- | --- | --- |
| P1 | Force maximale de sécurité | Real (N) | 2 000 |
| P2 | Timeout par défaut d'une recette | Duration | 10 000 |
| P3 | Vitesse de retour repos | Percent | 50 |
| P4 | Vmax de l'axe presse | Real (mm/s) | 50 |

La position de repos est une position nommée de la station (composante Z). Les recettes de presse (étapes, vitesses, cibles, fenêtres) sont définies dans la HMI (page Configuration de station, objet Presse, onglet Recettes de presse) et déployées avec la configuration ; 16 recettes au plus par presse, 8 segments au plus par recette (segment : course cible ou force cible, vitesse, fenêtre de contrôle min/max de force et de course).

État affiché : `E1` force (N), `E2` course (mm), `E3` recette active.

| N° | Action | Paramètres | Résultats | Mouvement | Timeout défaut |
| --- | --- | --- | --- | --- | --- |
| 1 | Exécuter recette | a1 Recette de presse (Recipe) | r1 Jugement global, r2 Force finale (N), r3 Course finale (mm), r4 Force max (N) | oui | P2 |
| 2 | Retour repos | — | — | oui | 5 000 ms |
| 3 | Tarer la force | — | — | non | 2 000 ms |
| 4 | Lire force et course | — | r1 Force, r2 Course | non | immédiate |

La courbe force/course (1 000 points au plus) est enregistrée à chaque exécution et lue par la HMI pour la trace. Erreurs : 100 Force maximale dépassée (arrêt immédiat, défaut), 101 Recette interrompue, 102 Capteur de force hors plage, 103 Cible de la recette non atteinte, 104 Recette inconnue. Simulation : courbe synthétique, jugement bon à 97 %.

### 7.2 Visseuse — type 41 (interface à définir)

| Param. | Nom | Type | Défaut |
| --- | --- | --- | --- |
| P1 | Programme par défaut | Int | 1 |

| N° | Action | Paramètres | Résultats | Timeout défaut |
| --- | --- | --- | --- | --- |
| 1 | Sélectionner programme | a1 Programme (Recipe) | — | 2 000 ms |
| 2 | Visser | a1 Programme (optionnel) | r1 Jugement, r2 Couple final (N·m), r3 Angle final (°) | 10 000 ms |
| 3 | Dévisser | a1 Angle (°) | — | 10 000 ms |
| 4 | Lire dernier résultat | — | r1 à r3 | immédiate |

Erreurs : 100 Couple maximal dépassé, 101 Angle hors fenêtre, 102 Vis non prise, 103 Défaut de l'outil. État sûr : arrêt de l'outil.

### 7.3 Distributeur — type 42 (interface à définir)

Signaux : `S1` DO commande, `S2` AI pression (optionnel), `S3` DI niveau bas (optionnel).

| Param. | Nom | Type | Défaut |
| --- | --- | --- | --- |
| P1 | Type | Enum (Temps et pression, Volumétrique, Jet) | 0 |
| P2 à P9 | Doses n° 1 à 8 (durée en ms ou volume en µl selon P1) | Real | 0 |
| P10 | Durée de purge par défaut | Duration | 2 000 |

| N° | Action | Paramètres | Résultats | Timeout défaut |
| --- | --- | --- | --- | --- |
| 1 | Déposer | a1 Dose (Int 1 à 8) | r1 Jugement (si contrôle) | 5 000 ms |
| 2 | Amorcer | a1 Volume | — | 10 000 ms |
| 3 | Purger | a1 Durée | — | durée + 1 000 ms |
| 4 | Lire niveau | — | r1 Niveau (Enum : plein, normal, vide) | immédiate |

Erreurs : 100 Cartouche vide, 101 Pression hors plage, 102 Dépôt non détecté. État sûr : distribution coupée.

### 7.4 Marquage — type 43 (interface à définir)

| N° | Action | Paramètres | Résultats | Timeout défaut |
| --- | --- | --- | --- | --- |
| 1 | Sélectionner modèle | a1 Modèle (Recipe) | — | 2 000 ms |
| 2 | Marquer | a1 Contenu (Text, peut être l'identifiant de pièce) | — | 10 000 ms |
| 3 | Vérifier marquage | — | r1 Jugement | 3 000 ms |
| 4 | Lire état | — | r1 État (Enum : prêt, en marquage, défaut) | immédiate |

Erreurs : 100 Marqueur non prêt, 101 Capot ouvert, 102 Défaut de la source. État sûr : arrêt du marquage. Le capot laser est une sécurité de la station (type 51).

### 7.5 Chargeur vibrant — type 44

Matériel : `D` (Asycube ou autre, interface à préciser). Signaux : `S1` DI trémie vide (optionnel).

| Param. | Nom | Type | Défaut |
| --- | --- | --- | --- |
| P1 à P8 | Motifs de vibration n° 1 à 8 : durée par défaut | Duration | 300 |

| N° | Action | Paramètres | Résultats | Timeout défaut |
| --- | --- | --- | --- | --- |
| 1 | Vibrer | a1 Motif (Int 1 à 8), a2 Durée (optionnelle) | — | durée + 1 000 ms |
| 2 | Distribuer pièces | a1 Durée | — | durée + 1 000 ms |
| 3 | Vider | — | — | 10 000 ms |
| 4 | Lire niveau | — | r1 Niveau (plein, normal, vide) | immédiate |

Erreurs : 100 Trémie vide (avertissement, balise bleu clignotant), 101 Défaut du chargeur. État sûr : vibration arrêtée.

### 7.6 Palettiseur ou magasin — type 45

Signaux : `S1` DI plateau présent, `S2` DI magasin vide, `S3` DI magasin plein, `S4` DO présenter, `S5` DO évacuer (les mécanismes plus complexes deviennent un module avec vérins et axes enfants).

| Param. | Nom | Type | Défaut |
| --- | --- | --- | --- |
| P1 | Lignes | Int | 4 |
| P2 | Colonnes | Int | 6 |
| P3 | Pas en X | Real (mm) | 20 |
| P4 | Pas en Y | Real (mm) | 20 |
| P5 | Position nommée de la première case | Int (n° de position) | 0 |

| N° | Action | Paramètres | Résultats | Timeout défaut |
| --- | --- | --- | --- | --- |
| 1 | Présenter plateau suivant | — | — | 10 000 ms |
| 2 | Évacuer plateau | — | — | 10 000 ms |
| 3 | Position de la case | a1 Case (Int, 0 = prochaine libre) | r1 Position (Position), r2 N° de case | immédiate |
| 4 | Marquer case | a1 Case, a2 Occupée (Bool) | — | immédiate |
| 5 | Lire occupation | — | r1 Occupées, r2 Libres, r3 Plein (Bool) | immédiate |

Erreurs : 100 Magasin vide, 101 Magasin plein, 102 Plateau absent, 103 Plateau non évacué.

## 8. Objets de machine et logique

### 8.1 Balise — type 50

Signaux : `S1` DO rouge, `S2` DO orange, `S3` DO vert, `S4` DO bleu, `S5` DO buzzer (tous optionnels).

| Param. | Nom | Type | Défaut |
| --- | --- | --- | --- |
| P1 | Période de clignotement | Duration | 800 |
| P2 | Durée par défaut du buzzer | Duration | 0 = jusqu'à acquittement |

Pilotée automatiquement par l'état du séquenceur auquel elle est rattachée (table du § 8 de `02-specifications-fonctionnelles.md`). Actions, en mode manuel seulement : 1 Tester (a1 Couleur Enum, a2 Durée), 2 Buzzer (a1 Durée). État affiché : `E1` couleurs allumées (bits).

### 8.2 Sécurités — type 51

Signaux : `S1..S8` DI, un par circuit (retour d'état du relais ou de l'automate de sécurité, 1 = OK). Le circuit reste câblé ou sur automate de sécurité : l'objet lit, ne pilote rien et ne permet aucun contournement.

| Param. | Nom | Type |
| --- | --- | --- |
| P1 à P8 | Type du circuit n (Enum : arrêt d'urgence, porte, barrière, capot, autre) | Enum |
| P9 à P16 | Acquittement requis pour le circuit n | Bool |
| P17 | Temps de filtrage | Duration |

Un objet Sécurités au niveau machine agit sur la machine ; dans une station, il agit sur la station seulement. Les noms des circuits sont dans la configuration (HMI).

État affiché : `E1` circuits OK (bits), `E2` circuits à acquitter (bits).

| N° | Action | Paramètres | Résultats | Timeout défaut |
| --- | --- | --- | --- | --- |
| 1 | Lire état | a1 Circuit (Int, 0 = tous) | r1 OK (Bool) | immédiate |
| 2 | Acquitter | a1 Circuit (0 = tous) | — | 1 000 ms |
| 3 | Attendre OK | a1 Circuit | — | 30 000 ms |

Erreur : 100 Circuit ouvert (sous-code n° de circuit) : défaut machine ou de station selon l'emplacement de l'objet ; 101 Incohérence de canaux ; 102 Acquittement requis.

### 8.3 Temporisation — type 52

| Param. | Nom | Type | Défaut |
| --- | --- | --- | --- |
| P1 | Durée par défaut | Duration | 1 000 |

| N° | Action | Paramètres | Résultats | Timeout |
| --- | --- | --- | --- | --- |
| 1 | Attendre | a1 Durée (défaut P1) | — | aucun |

La palette Logique propose aussi « Attendre durée » sans déclarer d'objet : le compilateur utilise une temporisation implicite de la station.

### 8.4 Compteur — type 53

| Param. | Nom | Type | Défaut |
| --- | --- | --- | --- |
| P1 | Valeur de départ | Int | 0 |
| P2 | Valeur maximale (0 = aucune) | Int | 0 |
| P3 | Remise à zéro au changement d'OF | Bool | 1 |
| P4 | Persistant (garde sa valeur après coupure) | Bool | 1 |

État affiché : `E1` valeur.

| N° | Action | Paramètres | Résultats | Timeout |
| --- | --- | --- | --- | --- |
| 1 | Incrémenter | a1 Pas (Int, défaut 1) | r1 Valeur | immédiate |
| 2 | Lire | — | r1 Valeur | immédiate |
| 3 | Remettre à zéro | — | — | immédiate |
| 4 | Comparer | a1 Valeur | r1 Atteint (Bool) | immédiate |

Erreur : 100 Valeur maximale dépassée (défaut de station, par exemple un nombre maximal de cycles avant maintenance). La surveillance des rebuts n'utilise pas de compteurs : elle est intégrée au PLC (`10-surveillance-des-rebuts.md`).

## 9. Modules

### 9.1 Groupe — type 100

Module libre : il contient n'importe quels objets, n'a ni paramètres propres ni fonctions ; il a l'override commun. Il sert à organiser, à régler un override commun et à donner un état agrégé (le plus grave de ses enfants : En erreur > Non initialisé > En cours > Prêt).

### 9.2 Robot cartésien XYZC — type 101

**Rôles d'enfant** :

| Rôle | Type accepté | Obligatoire |
| --- | --- | --- |
| `AxisX` | Axe linéaire (composante X) | oui |
| `AxisY` | Axe linéaire (composante Y) | oui |
| `AxisZ` | Axe linéaire (composante Z) | oui |
| `AxisC` | Axe rotatif | non |
| `Tool` | Aspiration et soufflage, ou Pince | oui |

**Paramètres** :

| Param. | Nom | Type | Défaut |
| --- | --- | --- | --- |
| P1 | Hauteur Z de sécurité | Real (mm) | −5 |
| P2 | Hauteur d'approche par défaut | Real (mm) | 10 |
| P3 | Vitesse d'approche | Speed | V1 |
| P4 | Sens de Z | Enum (Z négatif vers le bas, Z positif vers le bas) | 0 |

Le robot a l'override commun ; il s'applique à tous ses axes.

**Verrouillage généré** : les actions de mouvement de `AxisX`, `AxisY` et `AxisC` exigent Z au-dessus de la hauteur de sécurité (position Z ≥ P1 − fenêtre de position, selon P4). Le validateur avertit si une position nommée utilisée par le robot a un Z au-dessus de la hauteur de sécurité (incohérence).

**Fonctions de module** (dépliées à la compilation ; `pos` est un paramètre Position, `appr` une hauteur d'approche, `v` une vitesse) :

| Fonction | Paramètres | Déroulement |
| --- | --- | --- |
| Aller à | pos, v (défaut V2) | 1. Z → sécurité (si en dessous). 2. En parallèle : X → pos.X, Y → pos.Y, C → pos.C (si C existe). 3. Z → pos.Z |
| Aller au-dessus | pos, v | Comme Aller à, sans l'étape 3 |
| Prendre pièce | pos, appr (défaut : celle de la position, sinon P2), v | 1. Aller au-dessus de pos. 2. Z → pos.Z + appr (v). 3. Z → pos.Z (P3). 4. Outil : Aspirer (attendre le vide) ou Serrer sur pièce. 5. Z → sécurité (v). 6. Outil : Vérifier présence pièce → r1 Pièce présente |
| Poser pièce | pos, appr, soufflage (Bool, défaut 1), v | 1. Aller au-dessus de pos. 2. Z → pos.Z + appr. 3. Z → pos.Z (P3). 4. Outil : Souffler (si soufflage) puis Couper, ou Ouvrir. 5. Z → sécurité |
| Dégager | — | Z → sécurité |
| Référencer | — | Power on des axes en parallèle, puis Init par ordre de référencement (Z d'abord) |

L'ingénieur process peut copier une fonction (par exemple Prendre pièce) pour créer une fonction personnalisée, puis modifier ses étapes dans l'éditeur ; elle apparaît dans la palette du robot avec un badge « personnalisée ».

## 10. Ajouter un type d'objet

1. Écrire sa fiche ici, avec un numéro de type libre et toutes ses numérotations.
2. L'ajouter au fichier catalogue de la HMI (le test de cohérence du catalogue doit passer).
3. Écrire son modèle de simulation dans le simulateur de la HMI.
4. Écrire son FB PLC selon `04-plc-architecture-objets.md`, l'ajouter au pool et régénérer les constantes PLC depuis le catalogue.
