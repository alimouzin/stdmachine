# StdMachineLib : bibliothèque PLC TwinCAT 3

Bibliothèque générique du PLC (`docs/04` à `docs/06`, `docs/10`, `docs/12`, `docs/14`). Écrite **sans compilateur TwinCAT** : elle n'a jamais été compilée. Attendez-vous à des erreurs de syntaxe ou de typage à corriger au premier import.

## Dossiers

| Dossier | Contenu |
| --- | --- |
| `src/` | Sources ST lisibles, une par objet TwinCAT. **C'est ici qu'on modifie.** |
| `StdMachineLib/` | Fichiers TwinCAT générés (`.TcPOU`, `.TcDUT`, `.TcGVL`, `.TcIO`), à importer. Ne pas modifier à la main. |
| `src/00_Constants/GVL_Catalog.st` | Généré depuis `catalog/catalog.json` : ne pas modifier à la main. |

Sous-dossiers, comme dans `docs/04` § 2 : `00_Constants`, `01_Types`, `02_Interfaces`, `03_Base`, `04_Objects`, `05_Sequencers`, `06_HmiInterface`, `07_Tests`.

## Régénérer

```
python3 tools/plc/gen_catalog.py      # catalog/catalog.json -> plc/src/00_Constants/GVL_Catalog.st
python3 tools/plc/tcgen.py            # plc/src -> plc/StdMachineLib
```

Les identifiants (GUID) sont dérivés des noms : deux générations donnent les mêmes fichiers.

## Format des sources

- Un fichier `.st` par objet : `TYPE … END_TYPE` (DUT), `VAR_GLOBAL … END_VAR` (GVL, nom de fichier = nom de la GVL), `INTERFACE`, `FUNCTION`, `FUNCTION_BLOCK`.
- Dans un bloc, chaque méthode va de `METHOD …` à `END_METHOD`, chaque propriété de `PROPERTY …` à `END_PROPERTY` avec `GET … END_GET` (et `SET … END_SET`). Le corps du bloc est écrit avant la première méthode.
- La déclaration s'arrête au dernier `END_VAR` qui précède la première instruction.

## Importer dans TwinCAT

1. Créer un projet PLC de bibliothèque `StdMachineLib` (TwinCAT 3.1.4024 ou plus récent).
2. Ajouter les bibliothèques **Tc2_Standard**, **Tc2_System**, **Tc2_MC2** et **Tc2_Utilities** (heure locale de l'identifiant de pièce). Tc3 Planar viendra avec le pilote planaire réel.
3. Clic droit sur le projet › Ajouter › Élément existant, puis choisir les fichiers de `StdMachineLib/` dossier par dossier, en gardant la même arborescence.
4. Compiler et corriger. Signaler chaque correction (fichier, ligne, message) pour qu'elle soit reportée dans `src/`, sinon la prochaine génération l'écrase.

## Projet de la machine

Le projet de la machine (`StdMachinePlc`, `docs/04` § 2) déclare l'image E/S et les pools matériels, les donne à la bibliothèque, puis appelle `FB_Machine` à chaque cycle :

```iecst
PROGRAM MAIN
VAR
    fbMachine : FB_Machine;
    // fbPools : FB_MesPools;          // facultatif : pools aux tailles de la machine (I_ObjectPools)
    // fbPlanar : FB_PlanarTc3;        // facultatif : pilote planaire réel (I_PlanarDriver), sinon simulé
    bFirst : BOOL := TRUE;
END_VAR

IF bFirst THEN
    // GVL_IO : aDI AT %I* : ARRAY[0..GVL_Const.MAX_DI - 1] OF BOOL; … (mêmes dimensions que GVL_Const)
    GVL_IoRef.pDI := ADR(GVL_IO.aDI);
    GVL_IoRef.pDO := ADR(GVL_IO.aDO);
    GVL_IoRef.pAI := ADR(GVL_IO.aAI);
    GVL_IoRef.pAO := ADR(GVL_IO.aAO);
    GVL_IoRef.pAxis := ADR(GVL_Hw.aAxis);      // ARRAY[1..GVL_Const.MAX_AXES] OF AXIS_REF
    GVL_IoRef.nAxes := 0;                      // nombre d'axes reliés
    // fbMachine.SetPools(fbPools);
    // fbMachine.SetPlanarDriver(fbPlanar);
    bFirst := FALSE;
END_IF
fbMachine();
```

La tâche PLC doit avoir un temps de cycle de 1 à 10 ms. La zone active (`GVL_Active`, environ 2,3 Mo) et les contextes des movers sont persistants : vérifier la taille autorisée des données persistantes sur la cible.

## État d'avancement

| Partie | État |
| --- | --- |
| Constantes, catalogue généré | Écrit |
| Types, dont toutes les structures d'échange HMI–PLC | Écrit (ils font foi pour la disposition à l'octet près, D92) |
| Interfaces, `FB_ObjectBase`, `FB_NodeBase`, alarmes, traces, accès E/S | Écrit |
| Objets : entrées et sorties, vérin, axe, aspiration, pince, temporisation, compteur, balise, sécurités, groupe | Écrit |
| `FB_PackML` | Écrit |
| Interpréteur, séquenceur de station, canal manuel, pas à pas, calibrations de station | Écrit (`FB_Station`, `FB_ManualChannel`) |
| Séquenceur général, OF, fin des pièces, variables de mover, mover à vider et hors service | Écrit (`FB_GeneralSequencer`) |
| Gestionnaire de movers, pilote planaire simulé | Écrit (`FB_MoverManager`, `FB_PlanarSim`) ; pilote Tc3 Planar réel à écrire (`I_PlanarDriver`) |
| Surveillance des rebuts | Écrit (`FB_QualityMonitor`) |
| Bloc de tête : machine d'états, commandes, forçages, balise, identité, battement HMI | Écrit (`FB_Machine`) |
| Chargeur, sommes de contrôle, fabrique d'objets, pools, restauration et publication des valeurs rémanentes | Écrit (`FB_Loader`, `FB_Crc32`, `FB_ObjectFactory`, `FB_DefaultPools`) |
| Calibration machine | Écrit (`FB_MachineCalib`) |
| Compteurs de production et de temps (`FB_PlantPublisher`, `GVL_Plant`) | À écrire |
| Objets à communication (vision, Keyence, lecteur, presse, chargeur vibrant, magasin, robot Stäubli…) | À écrire ; protocoles à définir pour Stäubli, visseuse, distributeur, marquage |
| Tests TcUnit | À écrire |

## Hypothèses à confirmer

- Aspiration (`FB_VacuumBlow`) : le capteur de vide analogique donne des dixièmes de kPa (valeur brute ÷ 10). La fiche du catalogue ne dit pas l'échelle.
- Sécurités (`FB_Safety`) : un seul canal par circuit ; l'erreur 101 « Incohérence de canaux » n'est pas encore produite.
- Pince électrique : refusée à la configuration tant que son interface n'est pas définie.
- Axe : l'override passe par `MC_Power.Override` (au lieu de `MC_SetOverride` cité dans `docs/04` § 12) ; même effet, un bloc de moins.
- Types sans bloc dans la bibliothèque (système planaire et mover comme objets, robot externe, vision, palpeur, Asyril, pesage, lecteur, éclairage, presse, visseuse, distributeur, marquage, chargeur vibrant, magasin) : leur pool par défaut a une taille 0, un déploiement qui en contient est refusé (erreur 7).
- Écarts avec `docs/05` et `docs/06` adoptés comme le simulateur : voir D93 ; décisions propres à la bibliothèque : D94.

## Contrôles faits sans compilateur

`python3 tools/plc/check_st.py` passe quatre contrôles sur `src/` (ils ne remplacent pas le compilateur) : déclarations en double à la casse près (ST ne distingue pas `I` de `i`), accès à une variable interne d'un bloc depuis l'extérieur (seules entrées, sorties, méthodes et propriétés sont accessibles), membres inexistants dans les chaînes `a.b[i].c`, nombre et noms des arguments des appels de méthodes et de fonctions.
