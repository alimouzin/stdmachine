# Surveillance des rebuts

Ce document définit comment la machine surveille ses rebuts et comment elle réagit quand il y en a trop : ce qui est compté, à quel niveau (mover, station, fonction, machine, OF), sur quel intervalle, avec quels seuils, quelles réactions automatiques, et comment on reprend. Il couvre la règle fonctionnelle, l'algorithme PLC, l'interface HMI–PLC et les écrans. Les termes suivent `00-glossaire.md`.

## 1. Principes

1. **Détecter tôt une dérive**, avant qu'elle coûte une série de pièces : on surveille les rebuts **successifs** (réaction rapide à une panne franche) et le **taux** sur les dernières pièces (dérive lente).
2. **Isoler la cause au plus près** : on réagit d'abord sur l'élément en cause (un mover, une station) pour que le reste de la machine continue ; on n'arrête la machine que si le problème est général.
3. **Ne jamais confondre qualité et sécurité** : une réaction qualité ne fait jamais passer en Aborted. Elle met en pause, retire un élément de la production ou arrête le chargement, toujours en laissant finir les opérations en cours.
4. **Expliquer et tracer** : chaque réaction dit quel seuil a été franchi, sur quelles pièces ; chaque reprise est faite par une personne habilitée, avec un motif, et journalisée.
5. **Le PLC décide** : la surveillance tourne dans le PLC (et à l'identique dans le simulateur), pour réagir même si la HMI est arrêtée. La HMI affiche, règle les seuils et libère.

## 2. Ce qui compte comme rebut

Chaque fin d'opération sur une pièce et chaque clôture de pièce produit un **résultat** (bon ou mauvais) pour les portées concernées.

| Événement | Nature | Compte dans la surveillance |
| --- | --- | --- |
| Le verdict d'une pièce passe à mauvais pendant une opération (contrôle, mesure, politique « Pièce mauvaise », `SetVar` Verdict) | **Rebut constaté** | Oui, pour la station, la fonction et le mover de cette opération |
| Une pièce passée par une station est déclarée mauvaise plus tard par une fonction de contrôle qui impute ses rebuts | **Rebut imputé** | Oui, pour les stations amont qui ont travaillé la pièce (§ 7) |
| La pièce est retirée dans une station (rebut interne) | Rebut constaté | Oui, comme ci-dessus |
| La pièce reçoit le verdict mauvais parce que sa station est passée Aborted | **Rebut sur défaut** | Non pour les successifs et les taux (le défaut a déjà son alarme) ; oui pour le total de l'OF et la machine |
| L'opérateur confirme le retrait des pièces d'un mover à vider | Retrait manuel | Non pour les successifs et les taux ; oui pour le total de l'OF |
| La pièce sort bonne, ou une opération se termine sans que le verdict passe à mauvais | Résultat bon | Oui, comme résultat bon pour les mêmes portées |

Un verdict déjà mauvais à l'arrivée dans une station ne compte pas de nouveau pour cette station : seule la station où le verdict **change** est concernée.

## 3. Portées

| Portée | Quels résultats | Ce qu'elle détecte |
| --- | --- | --- |
| **Station** (constatés) | Fins d'opération dans la station, toutes fonctions confondues, avec le détail par fonction pour le diagnostic | Une station qui dérive (outil usé, réglage, capteur) |
| **Station** (imputés) | Pièces travaillées par la station puis jugées par une fonction de contrôle qui impute | Une station qui produit mal sans s'en apercevoir (une des deux presses dérive, le contrôle le voit) |
| **Fonction** | Fins d'opération de la fonction, toutes stations confondues | Un problème commun aux stations de la fonction (lot de matière, recette) |
| **Mover** | Pièces portées par le mover, à leur clôture | Un mover défectueux (nid abîmé, mauvais positionnement) |
| **Machine** | Toutes les pièces, à leur clôture | Une dérive générale |
| **OF** | Toutes les pièces de l'OF depuis son lancement | Le dépassement du budget de rebuts de l'OF |

## 4. Indicateurs et intervalle

### 4.1 Rebuts successifs

Nombre de résultats mauvais d'affilée dans la portée ; un résultat bon le remet à zéro. Réaction rapide à une panne franche (pièce absente, outil cassé).

### 4.2 Taux sur fenêtre glissante

Proportion de résultats mauvais parmi les **N derniers résultats** de la portée.

**Décision : la fenêtre est en nombre de résultats, pas en temps.** Une fenêtre en temps se vide pendant les arrêts et compte peu de pièces quand la cadence baisse : elle alarme à tort après une pause ou trop tard à faible cadence. Une fenêtre en nombre juge toujours le même échantillon, quelle que soit la cadence.

- Le taux n'est évalué que si la fenêtre contient au moins un **échantillon minimal** (sinon il est affiché « en cours de remplissage »).
- Les fenêtres et compteurs sont **persistants** (une coupure ne les efface pas).
- Ils sont remis à zéro : quand la **référence produit** change (nouvel OF d'une autre référence ; un nouvel OF de la même référence garde l'historique) ; à la libération après une réaction (§ 6) ; manuellement par un utilisateur habilité, avec motif.

### 4.3 Budget de rebuts de l'OF

Nombre maximal ou pourcentage maximal de pièces mauvaises sur la quantité de l'OF, facultatif. Avertissement à 80 % du budget consommé, réaction à 100 %.

## 5. Seuils et réactions par défaut

Chaque indicateur a deux niveaux : **avertissement** (on informe, la production continue) et **action** (réaction automatique). Les valeurs ci-dessous sont les défauts de la machine ; l'ingénieur process les règle dans le studio, globalement et par station ou par fonction.

| Portée | Successifs : avert. / action | Fenêtre (échantillon min.) | Taux : avert. / action | Réaction à l'action |
| --- | --- | --- | --- | --- |
| Station, constatés | 2 / 3 | 50 opérations (20) | 5 % / 10 % | Arrêt qualité de la station |
| Station, imputés | 3 / 5 | 50 pièces jugées (20) | 5 % / 10 % | Arrêt qualité de la station |
| Fonction | — / 5 | 100 opérations (30) | 4 % / 8 % | Arrêt du chargement |
| Mover | 2 / 3 | 20 pièces (10) | 10 % / 20 % | Mover hors service |
| Machine | 3 / 5 | 200 pièces (50) | 3 % / 5 % | Successifs : Arrêter la machine ; taux : Arrêt du chargement |
| OF | — | tout l'OF | 80 % / 100 % du budget | Arrêt du chargement |

« — » : pas de niveau d'avertissement par défaut (désactivable aussi pour toute ligne). Une valeur 0 désactive un seuil.

### 5.1 Réactions possibles

| Réaction | Effet | Ce qui continue |
| --- | --- | --- |
| Avertissement seulement | Alarme qualité de niveau avertissement, toast, compteur orange | Tout |
| **Arrêt qualité de la station** | La station termine son opération en cours puis passe Held ; elle sort du routage de toutes ses fonctions. Si une fonction n'a plus de station disponible, ses movers attendent en file ou en tampon | Les autres stations, les movers |
| **Mover hors service** | Le mover termine son trajet en cours ; ses pièces suivent leur process jusqu'à leur sortie ou leur rebut ; puis, vide, il va en parcage et ne reçoit plus de pièce | Les autres movers |
| **Arrêt du chargement** | Les opérations des fonctions de rôle Entrée ne sont plus lancées (aucune nouvelle pièce) ; les pièces en cours terminent leur process | Tout le reste |
| **Arrêter la machine** | Commande Stop de la machine (les opérations en cours se terminent) | Rien après la fin des opérations |

Toute réaction de niveau action : alarme de gravité **Qualité**, balise en **bleu clignotant** (opérateur attendu), bandeau dans la HMI qui nomme la portée, l'indicateur, la valeur et le seuil (« Pressage 02 : 3 rebuts successifs (seuil 3) — Arrêt qualité »). Chaque réaction est réglable par portée (par exemple remplacer « Arrêt qualité de la station » par « Avertissement seulement » sur une station de contrôle).

## 6. Libération et surveillance renforcée

- Une réaction se lève par une **libération qualité** : bouton « Libérer » (appui maintenu) sur l'alarme ou dans la page Qualité, réservé au droit `quality.release` (niveau Régleur par défaut). L'utilisateur choisit un motif (Réglage effectué, Outil changé, Matière changée, Fausse alarme, Autre) et peut ajouter un commentaire. La libération est journalisée.
- À la libération, les successifs et la fenêtre de la portée sont remis à zéro, puis la portée passe en **surveillance renforcée** pour ses 10 résultats suivants : l'action se déclenche dès 2 rebuts successifs. Après 10 résultats sans réaction, retour aux seuils normaux.
- La libération remet l'élément en production : la station repasse en Execute (Start de la station), le mover revient dans le process (étape 1), le chargement reprend, la machine peut être redémarrée.
- Un avertissement s'acquitte simplement (droit `alarm.ack`) ; il ne remet aucun compteur à zéro.

## 7. Imputation aux stations amont

Une fonction de contrôle (par exemple Contrôle dans MX-4) juge des pièces fabriquées avant elle. Quand deux stations interchangeables ont travaillé les pièces (Pressage 01 et 02), le contrôle seul ne dit pas laquelle dérive.

- Une fonction a l'option **« Imputer les rebuts aux stations amont »** et la liste des fonctions amont concernées (par défaut : toutes les fonctions de rôle Travail placées avant elle dans le process général).
- Chaque pièce garde la liste des opérations réalisées sur elle (station et fonction, 8 au plus).
- Quand une fonction qui impute juge une pièce : pour chaque opération amont de la pièce dans une fonction concernée, la station qui l'a faite reçoit un résultat **imputé** (bon si la pièce est jugée bonne, mauvais sinon).
- Exemple : Pressage 02 dérive. Le contrôle rejette 6 des 50 dernières pièces passées par Pressage 02 (12 %) et 1 des 50 passées par Pressage 01 (2 %). Pressage 02 passe en arrêt qualité ; Pressage 01 continue seule les deux fonctions Pressage pignon et Sertissage.

## 8. Alarmes

| Code | Alarme | Portée |
| --- | --- | --- |
| 950 | Rebuts successifs | Station (constatés) |
| 951 | Taux de rebut | Station (constatés) |
| 952 | Rebuts imputés successifs | Station (imputés) |
| 953 | Taux de rebuts imputés | Station (imputés) |
| 954 | Rebuts successifs / taux de rebut | Fonction |
| 955 | Rebuts successifs / taux de rebut | Mover |
| 956 | Rebuts successifs | Machine |
| 957 | Taux de rebut | Machine |
| 958 | Budget de rebuts de l'OF | OF |

Le sous-code porte la valeur atteinte (successifs, ou taux en dixièmes de %). La gravité est Avertissement au niveau avertissement, **Qualité** au niveau action (nouvelle gravité : son effet est la réaction choisie, son acquittement est la libération).

## 9. Algorithme PLC — FB_QualityMonitor

Un `FB_QualityMonitor` dans `FB_Machine`, appelé par le séquenceur général à chaque fin d'opération et à chaque clôture de pièce (`05-plc-sequenceurs.md` § 5.5). Données persistantes.

```iecst
TYPE E_QualityScope : (Station := 0, StationImputed := 1, Group := 2, Mover := 3, Machine := 4, Order := 5) USINT; END_TYPE
TYPE E_QualityReaction : (WarnOnly := 0, StationHold := 1, MoverOut := 2, StopLoading := 3, MachineStop := 4) USINT; END_TYPE
TYPE E_QualityLevel : (Normal := 0, Warning := 1, Action := 2, Probation := 3) USINT; END_TYPE

TYPE ST_QualityRule :                 // réglage d'une portée (défauts machine, surchargés par station / fonction)
STRUCT
    nConsecWarn, nConsecAction : USINT;       // 0 = désactivé
    nWindow, nMinSample         : USINT;      // 1..255
    fRateWarn, fRateAction      : REAL;       // en %, 0 = désactivé
    eReactionConsec, eReactionRate : E_QualityReaction;
END_STRUCT END_TYPE

TYPE ST_QualityTrack :                // état d'une instance de portée
STRUCT
    nConsec      : USINT;
    aWindow      : ARRAY[0..31] OF BYTE;      // 256 résultats en bits, tampon circulaire (1 = mauvais)
    nHead, nCount: USINT;
    nBadInWindow : USINT;
    nProbationLeft : USINT;                   // résultats restants en surveillance renforcée
    eLevel       : E_QualityLevel;
    bLocked      : BOOL;                      // réaction en cours, attend une libération
    nTotalGood, nTotalBad : UDINT;            // depuis la dernière remise à zéro
END_STRUCT END_TYPE
```

Instances : 16 stations × 2 (constatés, imputés), 16 fonctions, 32 movers, 1 machine, 8 OF (un budget par OF de la table des OF, `05` § 5.6).

**À chaque résultat** `Record(eScope, nIndex, bBad)` :

```iecst
t REF= aTrack[eScope, nIndex];  r REF= Rule(eScope, nIndex);
// fenêtre glissante
IF t.nCount = r.nWindow THEN                       // retirer le plus ancien
    IF BitAt(t, (t.nHead + 256 - t.nCount) MOD 256) THEN t.nBadInWindow := t.nBadInWindow - 1; END_IF
ELSE
    t.nCount := t.nCount + 1;
END_IF
SetBit(t, t.nHead, bBad);  t.nHead := (t.nHead + 1) MOD 256;
IF bBad THEN t.nBadInWindow := t.nBadInWindow + 1; t.nConsec := t.nConsec + 1; t.nTotalBad := t.nTotalBad + 1;
ELSE t.nConsec := 0; t.nTotalGood := t.nTotalGood + 1; END_IF

// évaluation
nConsecAct := SEL(t.nProbationLeft > 0, r.nConsecAction, 2);
fRate := SEL(t.nCount >= r.nMinSample, -1.0, 100.0 * t.nBadInWindow / t.nCount);
bActConsec := nConsecAct > 0 AND t.nConsec >= nConsecAct;
bActRate   := r.fRateAction > 0 AND fRate >= r.fRateAction AND t.nProbationLeft = 0;
bWarn      := (r.nConsecWarn > 0 AND t.nConsec >= r.nConsecWarn) OR (r.fRateWarn > 0 AND fRate >= r.fRateWarn);
IF t.nProbationLeft > 0 THEN t.nProbationLeft := t.nProbationLeft - 1; END_IF

IF (bActConsec OR bActRate) AND NOT t.bLocked THEN
    t.bLocked := TRUE; t.eLevel := E_QualityLevel.Action;
    React(eScope, nIndex, SEL(bActConsec, r.eReactionRate, r.eReactionConsec));
    RaiseQualityAlarm(eScope, nIndex, bActConsec, Severity := Quality);
ELSIF bWarn AND t.eLevel = E_QualityLevel.Normal THEN
    t.eLevel := E_QualityLevel.Warning;
    RaiseQualityAlarm(eScope, nIndex, bActConsec, Severity := Warning);
ELSIF NOT bWarn AND t.eLevel = E_QualityLevel.Warning THEN
    t.eLevel := SEL(t.nProbationLeft > 0, E_QualityLevel.Normal, E_QualityLevel.Probation);
ELSIF t.eLevel = E_QualityLevel.Probation AND t.nProbationLeft = 0 AND NOT t.bLocked THEN
    t.eLevel := E_QualityLevel.Normal;
END_IF
```

Un déploiement qui change la règle d'une portée (fenêtre, seuils) remet cette portée à zéro ; les autres gardent leur historique.

**Appels** :
- Fin d'opération dans la station S, fonction F, mover M, pour chaque pièce traitée : `Record(Station, S, bad)`, `Record(Group, F, bad)` ; `bad` = le verdict est passé à mauvais pendant cette opération (pas sur défaut). Si F impute : pour chaque opération amont concernée de la pièce, `Record(StationImputed, station amont, verdict mauvais)`.
- Clôture d'une pièce (sortie, rebut, retrait interne) : `Record(Mover, M, bad)`, `Record(Machine, 0, bad)` ; le budget d'OF compte toutes les pièces mauvaises, y compris sur défaut et retirées à la main.
- Les rebuts sur défaut et les retraits manuels n'appellent `Record` que pour l'OF.

**Réactions** (`React`) :
- `StationHold` : `bQualityLock` de la station := TRUE ; le séquenceur général donne Hold à la station ; une station verrouillée n'est plus candidate au routage (`05` § 5.4) et ne reprend Execute qu'après libération.
- `MoverOut` : `bQualityLock` du mover := TRUE ; à sa prochaine instruction `End` (ou dès qu'il est vide), il va en parcage et passe `OutOfService`.
- `StopLoading` : `bLoadingLocked` := TRUE ; même effet que la fin d'OF sur les instructions `Route` vers une fonction de rôle Entrée, avec le motif « Arrêt qualité ».
- `MachineStop` : commande Stop de la machine, motif « Arrêt qualité ».
- Toute réaction allume le bleu clignotant de la balise machine jusqu'à la libération.

**Libération** (commande HMI) : vérifie la portée verrouillée, remet `nConsec`, la fenêtre et `nBadInWindow` à zéro, `nProbationLeft := 10`, `eLevel := Probation`, `bLocked := FALSE`, lève le verrou de l'élément (station : Start de la station si la machine est en Execute ; mover : retour `Ready` à l'étape 1 ; chargement : reprise ; machine : rien, l'opérateur redémarre), efface l'alarme.

## 10. Interface HMI–PLC

- Configuration déployée dans `ST_MachineData` : `stQualityDefaults : ARRAY[E_QualityScope] OF ST_QualityRule`, `aStationRule[1..16]`, `aGroupRule[1..16]` (avec un drapeau « hérite des défauts »), `aGroupImpute[1..16] : UINT` (masque des fonctions amont à imputer, 0 = n'impute pas), `stOrderBudget` (nombre ou %).
- Statut lu par la HMI à 2 Hz : `GVL_Hmi.stQuality` (toutes les `ST_QualityTrack`, sans le détail des bits pour les movers ; fenêtres complètes des stations et de la machine pour l'affichage).
- Commandes (handshake habituel) : `GVL_Hmi.stQualityCmd` : libérer (portée, index, motif), remettre à zéro (portée, index, motif). L'utilisateur et le motif sont journalisés par la HMI.
- Chaque pièce garde `aOps : ARRAY[1..8] OF ST_PartOp` (station, fonction, verdict après l'opération), qui sert à l'imputation et à la trace.

## 11. Écrans

### 11.1 Page Qualité (exploitation, rail : icône `fact_check`)

```
┌─ Machine : 2,4 % sur 200 pièces ▕█████▁▁▁▁▏ seuils 3 % / 5 %   Successifs 0   OF 24-117 : 7 / 30 (23 %) ─┐
├──────────────────────────────────────────────────────────────────────────────────────────────────────────┤
│ Verrous en cours                                                                                       │
│ ⛔ Pressage 02 · Arrêt qualité · 3 rebuts successifs (seuil 3) · 14:21      [ Libérer (maintenir) ]      │
├───────────────────────────────┬──────────────────────────────────────────────────────────────────────────┤
│ Stations                      │ Fenêtre (50 dernières)                        Succ.  Taux    Imputés      │
│ Chargement                    │ ■■■■■■■■■■■■■■■■■■■■■■■■■■■■■■■■■■■■■■■■■■■■■■■ │  0    0,0 %     —          │
│ Pressage 01  [Pressage pignon]│ ■■■■■■■■■■■■■■■■■■■■■□■■■■■■■■■■■■■■■■■■■■■■■■■ │  0    2,0 %   2,0 %        │
│ Pressage 02  ⛔                │ ■■■■■■■■■■■■■■■■■■■■■■■■■■■■■■■■■■■■■□■■■■■□□□  │  3   10,0 %  12,0 % ▲     │
│ …                             │                                                                          │
├───────────────────────────────┴──────────────────────────────────────────────────────────────────────────┤
│ Fonctions (tableau identique) · Movers (grille de tuiles : n°, mini-fenêtre de 20, état, hors service)    │
│ Répartition des rebuts de la fenêtre machine : par station, par fonction, par alarme (barres horizontales)│
└──────────────────────────────────────────────────────────────────────────────────────────────────────────┘
```

- **Fenêtre** : une rangée de petits carrés (un par résultat, le plus récent à droite), vert bon, rouge mauvais, gris vide ; repères verticaux aux seuils de taux convertis en nombre de rebuts ; la rangée défile vers la gauche à chaque nouveau résultat (animation 200 ms).
- **Successifs** : pastille numérique, orange au seuil d'avertissement, rouge au seuil d'action.
- **Taux** : valeur, flèche de tendance (comparaison avec la moitié ancienne de la fenêtre), « en cours de remplissage 12/20 » sous l'échantillon minimal.
- **État de la portée** : normal (neutre), avertissement (fond orange), action (rouge, icône qui clignote en phase avec la balise), surveillance renforcée (contour orange pointillé et « 3 / 10 »).
- **Libérer** : appui maintenu, puis feuille de motif (choix en gros boutons + commentaire facultatif) ; droit `quality.release`.
- **Remettre à zéro** (menu par appui long sur une ligne) : droit `quality.reset`, motif obligatoire.
- Toucher une ligne → tiroir de détail : les dernières pièces mauvaises de la portée (identifiant, heure, opération, données, lien vers l'historique de la pièce), détail par fonction pour une station partagée.

### 11.2 Ailleurs dans l'interface

- **Page Machine** : une station en arrêt qualité porte un macaron « Arrêt qualité » rouge et ses positions de file grisées ; un mover hors service est affiché en gris avec une croix ; la carte OF montre le budget de rebuts consommé.
- **Page Station** : bandeau « Arrêt qualité » avec la raison et le bouton Libérer ; pastille successifs et taux dans l'en-tête.
- **Barre du haut** : la cloche compte aussi les alarmes qualité ; une puce bleue « Chargement arrêté (qualité) » apparaît quand le chargement est verrouillé.
- **Studio machine › Qualité** : tableau des seuils par portée (défauts), surcharges par station et par fonction (ligne héritée en gris, surcharge en noir), réaction par indicateur, option d'imputation par fonction avec ses fonctions amont, budget d'OF par défaut.

### 11.3 Droits ajoutés

| Droit | Code | Niveau par défaut |
| --- | --- | --- |
| Libérer après une réaction qualité | `quality.release` | 1 |
| Remettre à zéro les compteurs qualité | `quality.reset` | 2 |
| Régler les seuils qualité | `quality.config` | 2 |

## 12. Validation et tests

Règles de validation ajoutées :
- V019 (erreur) : seuil d'avertissement supérieur ou égal au seuil d'action ; échantillon minimal supérieur à la fenêtre ; fenêtre hors de 1..255.
- V113 (avertissement) : fonction qui impute sans fonction amont de rôle Travail avant elle dans le process.
- V114 (avertissement) : réaction « Arrêt qualité de la station » sur une station qui est seule dans une de ses fonctions (sa pause bloquera cette fonction).

Tests (simulateur, puis TcUnit) : fenêtre glissante (remplissage, rotation, échantillon minimal) ; successifs et remise à zéro par un bon ; chaque réaction et sa libération ; surveillance renforcée (déclenchement à 2) ; imputation à deux stations amont ; rebuts sur défaut exclus des taux ; remise à zéro au changement de référence ; persistance après redémarrage ; scénario MX-4 : taux de rebut de la presse de Pressage 02 forcé à 15 % → arrêt qualité de Pressage 02 par imputation, la production continue sur Pressage 01.
