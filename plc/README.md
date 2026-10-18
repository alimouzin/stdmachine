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
2. Ajouter les bibliothèques **Tc2_Standard**, **Tc2_System** et **Tc2_MC2** (Tc3 Planar viendra avec le gestionnaire de movers).
3. Clic droit sur le projet › Ajouter › Élément existant, puis choisir les fichiers de `StdMachineLib/` dossier par dossier, en gardant la même arborescence.
4. Compiler et corriger. Signaler chaque correction (fichier, ligne, message) pour qu'elle soit reportée dans `src/`, sinon la prochaine génération l'écrase.

## Projet de la machine

Le projet de la machine (`StdMachinePlc`, `docs/04` § 2) déclare l'image E/S et les pools matériels, puis les donne à la bibliothèque au premier cycle :

```iecst
// GVL_IO : aDI AT %I* : ARRAY[0..GVL_Const.MAX_DI - 1] OF BOOL; … (mêmes dimensions que GVL_Const)
GVL_IoRef.pDI := ADR(GVL_IO.aDI);
GVL_IoRef.pDO := ADR(GVL_IO.aDO);
GVL_IoRef.pAI := ADR(GVL_IO.aAI);
GVL_IoRef.pAO := ADR(GVL_IO.aAO);
GVL_IoRef.pAxis := ADR(GVL_Hw.aAxis);      // ARRAY[1..GVL_Const.MAX_AXES] OF AXIS_REF
GVL_IoRef.nAxes := <nombre d'axes reliés>;
```

## État d'avancement

| Partie | État |
| --- | --- |
| Constantes, catalogue généré | Écrit |
| Types, dont toutes les structures d'échange HMI–PLC | Écrit (ils font foi pour la disposition à l'octet près, D92) |
| Interfaces, `FB_ObjectBase`, `FB_NodeBase`, alarmes, traces, accès E/S | Écrit |
| Objets : entrées et sorties, vérin, axe, aspiration, pince, temporisation, compteur, balise, sécurités, groupe | Écrit |
| `FB_PackML` | Écrit |
| Interpréteur, séquenceur de station, canal manuel, pas à pas | À écrire |
| Séquenceur général, movers (Tc3 Planar), OF, qualité, forçages | À écrire |
| Chargeur, fabrique d'objets, `GVL_Hmi`, identité, sauvegarde des valeurs | À écrire |
| Objets à communication (vision, Keyence, lecteur, presse, chargeur vibrant, magasin, robot Stäubli…) | À écrire ; protocoles à définir pour Stäubli, visseuse, distributeur, marquage |
| Tests TcUnit | À écrire |

## Hypothèses à confirmer

- Aspiration (`FB_VacuumBlow`) : le capteur de vide analogique donne des dixièmes de kPa (valeur brute ÷ 10). La fiche du catalogue ne dit pas l'échelle.
- Sécurités (`FB_Safety`) : un seul canal par circuit ; l'erreur 101 « Incohérence de canaux » n'est pas encore produite.
- Pince électrique : refusée à la configuration tant que son interface n'est pas définie.
- Axe : l'override passe par `MC_Power.Override` (au lieu de `MC_SetOverride` cité dans `docs/04` § 12) ; même effet, un bloc de moins.
