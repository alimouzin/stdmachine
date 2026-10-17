# Proposition : couloirs de l'éditeur et sorties des étapes

> **Proposition, non validée : ce document ne fait pas foi.** Il prépare une décision sur `docs/07` § 3.2, `docs/08` § 6.9 et `docs/00` (mot « Couloir »). Aucun code n'a été modifié. Écrit le 2026-10-17 d'après le code de `develop` et les captures du simulateur (`artifacts/screens/sim-running/ProcessEditor-*.png`).

## 1. Les couloirs sont-ils utiles ?

### Ce que montrent les captures

- **Station Pressage 01, process « Presser pignon »** : au moins 5 couloirs utilisés (Robot XYZC, Vision Asyril, Presse, Présence nid, Compteur), de 262 px, soit 1 310 px, plus le couloir Logique dès qu'on ajoute un Si ou un Attendre (les couloirs vides sont masqués, D45). Le canevas ne mesure qu'environ 980 px sur ces captures (environ 1 170 px en 1 920 × 1 080, une fois retirés le rail, la palette de 280 px et le panneau de droite de 380 px). L'éditeur défile donc en horizontal, et l'en-tête de la dernière colonne visible est coupé (« Compt… »).
- **Chemin de lecture** : « Presser pignon » va de la colonne 4 (Lire) à la colonne 2 (Chercher, Obtenir) puis à la colonne 1 (Prendre pièce). On lit une diagonale en escalier ; les connecteurs traversent toute la largeur.
- **Process général** : même escalier, sur des couloirs qui sont les fonctions. Or la carte écrit déjà « Fonction : Pressage pignon ».
- **Dans le code** : la notion de couloir n'existe pas dans le modèle. Le couloir se déduit de l'objet de l'étape. Les couloirs ne portent donc aucune information, seulement une mise en page (`ProcessLayout.cs`, 365 lignes, avec des cas particuliers comme D45, blocs qui débordent de leur couloir).

### Ce que les couloirs apportent vraiment

1. Voir d'un coup d'œil que deux branches en parallèle travaillent sur des objets différents.
2. Le chronogramme (une ligne par objet) et le suivi d'exécution (en-tête de couloir bleu ou rouge).
3. Une palette qui reprend l'arbre des objets.

Les points 1 et 3 s'obtiennent sans couloirs (pastille d'objet sur la carte, palette groupée par objet, ce qui existe déjà). Le point 2 est une vue du temps par objet, qui est sa place naturelle : en lecture seule, pas comme structure d'édition.

### Recommandation

**Éditer en flux vertical, sans couloirs.**

- Une seule colonne de cartes ; les colonnes n'apparaissent que **dans** un bloc (En parallèle, Si / Sinon), là où elles ont un sens.
- Chaque carte garde l'icône et le nom de son objet sous forme de pastille colorée par objet, donc l'information « qui fait quoi » ne disparaît pas.
- Le **chronogramme par objet** reste l'endroit où l'on voit la parallélisation dans le temps.
- Gains : plus de défilement horizontal pour une séquence, glisser-déposer plus simple (une seule colonne de cibles), moins de cas particuliers de mise en page, process général lisible (c'est surtout une suite avec des Si / Sinon).
- Coût : on perd la vue « un objet = une colonne » pendant l'édition ; il faut un autre signe pour le suivi d'exécution (pastille d'objet qui s'allume sur la carte, plus bandeau des objets actifs).

**Pour trancher sans débat** : ajouter un second mode de mise en page « Flux » à côté des couloirs (même modèle, seul `ProcessLayout` change), puis capturer « Presser pignon » et « Process général » dans les deux modes et comparer sur l'écran réel. Les documents ne sont modifiés qu'après votre choix. Ordre de grandeur : un prototype de mise en page, sans toucher au modèle ni au compilateur.

## 2. Sorties des étapes

### Ce qui existe

- Une action du catalogue a des **résultats fixes** `r1..r8` (`docs/03`), liés à une variable de station, une donnée de pièce, l'identifiant, le verdict ou « pièce retirée » (`ActionStep.Bindings`, `BindTarget`).
- **Asyril Eye+** : « Chercher pièce » donne le nombre de pièces (`r1`), « Obtenir pièce » donne la position (`r1`, type Position). Le besoin « une recherche Asyril fournit une position » est donc déjà couvert **action par action**, mais il faut créer une variable de station de type Position, enchaîner deux actions, gérer la répétition, lier le résultat, puis le relire dans l'étape suivante.
- **Fonction de module** (« Prendre pièce »…) : le catalogue lui donne des résultats (`CatalogFunction.Results`), mais `FunctionStep` n'a **pas de liaisons** : ils ne sont pas utilisables.
- **Fonction personnalisée, sous-process, opération** : des paramètres d'entrée (opération : 8 au plus), **aucune sortie**.
- **Keyence XG** : absent du catalogue (il y a IV et GT2, aux résultats fixes). Ses sorties sont à définir par l'ingénieur process : le catalogue ne sait pas les décrire.

### Proposition : des sorties nommées, rangées dans des variables nommées

Une **sortie** est un nom, un type (nombre, entier, booléen, position, jugement, texte) et une unité. Elle est déclarée là où elle a un sens (action ou fonction du catalogue, bloc composé, objet), puis **rangée dans une variable nommée**. Rien n'est implicite : la valeur a un nom et se voit dans l'éditeur.

**a) Variables nommées créées à la demande (le cœur).** En ajoutant une étape qui a des sorties, l'éditeur crée la variable et la nomme d'après la sortie (« Position de la pièce »), avec un nom modifiable d'un geste. Elle apparaît tout de suite dans l'onglet Données du process et dans les sélecteurs de valeur de toutes les étapes qui peuvent la lire.

- **Deux portées** : **variable de process** (visible seulement dans ce process ; par défaut) et **variable de station** (partagée entre les process de la station, comme aujourd'hui). Une variable de process est allouée par le compilateur dans la même plage de 64 variables que les variables de station.
- **Validation (nouvelle règle, dans l'esprit de V014)** : une variable nommée garde sa dernière valeur ; un avertissement signale sa lecture sur un chemin où elle n'est pas écrite avant (étape dans une branche Si non prise, ou après une politique d'échec « Ignorer »), ainsi qu'un type incompatible.
- Pas de changement du jeu d'instructions du PLC (`docs/06`) : tout se compile en variables et en `SetVar` ordinaires.

**b) Blocs composés avec sorties.**

- **Fonction personnalisée** (dépliée à la compilation) : une sortie est un résultat d'une de ses actions internes, relié à une « sortie de la fonction ». Au point d'appel, le compilateur remplace par la variable nommée choisie par l'appelant : aucun coût d'exécution.
- **Sous-process et opération** (appelés, pas dépliés) : la sortie est écrite dans un emplacement interne du bloc (simple passage de valeur, que l'ingénieur ne manipule pas), copié vers la **variable nommée choisie à l'appel** juste après le retour (une instruction de copie). Nouvelle règle : un bloc à sorties ne peut pas être appelé en même temps depuis deux contextes (deux branches parallèles, ou opération et cycle de fond).
- **Fonctions de module du catalogue** : ajouter les liaisons de résultats à `FunctionStep` (même mécanisme).
- Exemple visé : un bloc « Rechercher pièce Asyril » = chercher (refaire 3 fois avec vibration), puis obtenir ; sortie « Position pièce » (Position) et « Nombre » (Entier).

**c) Objets à sorties variables (Keyence XG).** Un type du catalogue porte l'indicateur `variableOutputs` sur son action de lecture (« Lire résultats »). L'objet, dans le Studio station, a une table « Sorties » : nom, type, unité, plage de simulation, emplacement source (opaque pour l'instant). Les résultats de l'action sont alors la liste de l'instance. Le simulateur génère des valeurs selon la plage ; le protocole réel (EtherNet/IP ou TCP, décalages) se règle en phase 9, sans changer la HMI.

### Limites connues

- Le PLC garde **8 résultats** par action (`ST_ActResults` : `aReal[8]`, un seul `stPos`, un seul `sText`). Une caméra à plus de 8 valeurs, ou à deux positions, demande d'élargir cette structure (changement de `docs/04` et `docs/06`).
- Variables : visibles + cachées ≤ 64 par station. Les variables de process y comptent (allouées par le compilateur dans la même plage que les variables cachées actuelles). Première version : allocation simple et règle de validation claire. Évolution : partager la place entre variables de process dont les durées de vie ne se recoupent pas.

### Ordre de réalisation proposé

1. **Variables nommées sur les résultats existants** : interface des liaisons de résultats des actions (absente aujourd'hui) ; création à la demande d'une variable nommée à partir d'une sortie (Asyril position, Keyence IV jugement et valeur) ; variables de process (modèle, compilation, onglet Données, sélecteurs) ; règle de validation de la valeur périmée. Démontrable de bout en bout avec le simulateur : « Obtenir pièce » écrit `PosPignon`, « Prendre pièce » la lit.
2. **Variables de mover** (§ 5) : modèle, compilation, simulateur, validation, tiroir du mover.
3. **Sorties des fonctions personnalisées**, puis des sous-process et opérations.
4. **Objets à sorties variables** (type Keyence XG, table de sorties, simulation) : plus tard.

Chaque étape : tests de compilation (allocation, portée), tests de validation, test de simulation, captures regardées, journal de décisions.

### Documents touchés (après accord)

`docs/00` (mots « Sortie », « Variable de process », « Variable de mover »), `docs/02` § 5.2 et 5.3, `docs/03` (indicateur `variableOutputs`, type XG), `docs/04` (seulement si on dépasse 8 résultats), `docs/05` (structure du mover, `SetVar`) et `docs/06` § 1, § 2 et § 5 (sources et cibles `MoverData`, compilation), `docs/07` § 3.1, § 3.2 et § 4.2 (modèle, règles), `docs/08` § 6.9 (panneau Étape, sélecteurs, onglet Données) et tiroir du mover, `docs/09` (décisions).

## 3. Décisions du 2026-10-17

1. **« Bloc »** = une carte d'étape (action, fonction, sous-process, opération). Les blocs de structure (Si / Sinon, En parallèle, Répéter) n'ont pas de sortie propre. **Décidé.**
2. **Variables nommées, pas de valeurs implicites.** Une sortie d'étape est rangée dans une **variable nommée**, créée à la demande par l'éditeur (nom proposé d'après la sortie, modifiable d'un geste) et visible tout de suite. Deux portées : **variable de process** (visible seulement dans ce process, listée dans ses données, par défaut) et **variable de station** (partagée entre les process). **Décidé le 2026-10-17** (remplace les « valeurs d'étape » automatiques proposées d'abord).
3. **Plusieurs sorties par bloc, nommées ou non.** Une sortie sans nom se désigne par son rang (« Sortie 1 », « Sortie 2 »…) ; une sortie nommée garde son nom partout (cartes, sélecteurs, liaisons). Le Keyence XG est remis à plus tard : l'étape 3 de l'ordre de réalisation attend. **Décidé.**
4. **Couloirs : prototype du mode « Flux ».** Fait ; le Flux est le mode par défaut (D72) et les couloirs restent **en option**. La suppression éventuelle des couloirs est à décider plus tard (note dans `REPRISE-PHASES-2-A-5.md`).

## 4. Prototype « Flux » (réalisé le 2026-10-17)

- **Utilisation** : bascule « Vue : Couloirs | Flux » dans la barre de l'éditeur de process, ou option `--layout lanes` ou `--layout flow` (captures, page Station comprise ; le Flux est le défaut depuis D72). Le réglage est global à l'application (`Editor/EditorSettings.cs`) et n'est pas enregistré. Le modèle de process, le compilateur et le simulateur ne changent pas.
- **Ce qui change** (`Editor/ProcessLayout.cs`, `CardModels.cs`, `ProcessCanvasView.xaml`) : en mode Flux, pas d'en-têtes de couloirs ; les cartes sont empilées dans une seule colonne centrée ; chaque carte d'un objet porte une pastille (icône et nom de l'objet de premier niveau) ; un bloc En parallèle ou Si / Sinon épouse ses branches au lieu de prendre toute la largeur. Au niveau machine, il n'y a pas de pastille (la carte écrit déjà « Fonction : … »).
- **Captures de comparaison** (`artifacts/layout/`, non versionnées) : `presser-lanes.png` et `presser-flow.png`, `general-lanes.png` et `general-flow.png`, `station-flow.png` (suivi d'exécution).
- **Constats** :
  - « Presser pignon » tient en une colonne, sans défilement horizontal ; l'escalier de connecteurs a disparu.
  - Le process général se lit de haut en bas, avec ses deux Si / Sinon.
  - En suivi d'exécution, les 3 branches parallèles (Presse, Vision Asyril, Robot XYZC) sont côte à côte dans la vue, alors qu'en couloirs elles sont dans trois colonnes qui demandent du défilement.
  - Perdu : la lecture « une colonne par objet ». La pastille et le chronogramme par objet la remplacent.
- **Limites du prototype** : pastilles sans couleur propre à l'objet (les jetons de `Theme/Tokens.xaml` n'en définissent pas) ; glisser-déposer au doigt non essayé en mode Flux (la géométrie des zones de dépôt est celle des couloirs non alignés, déjà utilisée dans les blocs) ; défauts d'affichage déjà présents en couloirs et inchangés (texte de la carte « Exécuter recette » qui se chevauche, étiquette « Si … » qui chevauche un connecteur) ; pas de test automatique de la mise en page (le projet de tests ne référence pas la HMI).

## 5. Variables de mover (demande du 2026-10-17)

> **Spécifié de façon ferme le 2026-10-17 (D80)** : la référence est `docs/05` § 5.8 et `docs/02` § 5.3. Le texte ci-dessous est conservé comme historique.

**Besoin** : un mover doit pouvoir porter des informations propres, lisibles et modifiables par les stations, pour déplacer des informations d'une station à l'autre (un réglage choisi en amont, un offset propre à ce mover, un compteur de passages, un drapeau « à repasser »…).

**Ce qui existe** : les **données de pièce** (32 au plus, définies dans la configuration machine) sont attachées à la pièce d'un emplacement du mover, avec le Verdict et l'Identifiant. Elles suivent la pièce, disparaissent avec elle, et il y en a un jeu par emplacement (4 emplacements au plus). Elles ne couvrent donc pas une information qui appartient au **mover** lui-même et qui reste quand le mover est vide ou change de pièce.

**Décidé le 2026-10-17** : des **variables de mover**, construites comme les données de pièce, avec des positions.
- **Déclaration** : dans la configuration machine, une liste nommée (nom, type **nombre, booléen ou position**, unité, valeur initiale, règle de remise à zéro), commune à tous les movers ; chaque mover a **son propre jeu de valeurs**. Limite proposée : 16 variables.
- **Remise à zéro, au choix pour chaque variable** : **jamais** (la valeur survit au Stop, au Reset, à un nouveau déploiement et à un redémarrage du PLC, par exemple un offset propre au mover) ; **au déploiement** ; **quand le mover est vidé** (commande « Vider le mover »). Une étape « Réinitialiser les variables du mover » existe aussi. Aucune variable n'est remise à zéro par un simple Reset de la machine.
- **Accès** : une opération de station lit et écrit les variables du mover **présent à son poste** ; le process général peut les écrire aussi (étape « Définir variable de mover »). Le cycle de fond n'a pas de mover : leur usage y est refusé par la validation. Elles sont utilisables comme valeurs, dans les conditions, comme cibles de liaison d'un résultat et dans « Définir variable », exactement comme les variables de station et les données de pièce.
- **Compilation et PLC** : nouvelle source de valeur et nouvelle cible de liaison `MoverData` (à côté de `PartData`) ; un tableau de valeurs et un tableau de positions dans la structure du mover (`ST_Mover`) ; variables à règle « jamais » en mémoire rémanente sur le PLC réel. Changement de `docs/05` (structures, `SetVar`), de `docs/06` (`E_ValueSource`, `E_BindTarget`) et du simulateur.
- **Durée de vie** : les valeurs persistent d'un Stop à un Start, comme les movers eux-mêmes.
- **Déploiement d'un projet dont la liste de variables a changé** : une variable est reconnue par son identifiant stable (pas par son rang ni son nom). Une variable conservée garde sa valeur si son type n'a pas changé (même pour la règle « au déploiement », qui s'applique alors) ; une variable ajoutée prend sa valeur initiale ; une variable supprimée disparaît. Le déploiement affiche ce qui sera perdu.
- **HMI** : le tiroir d'un mover (page Machine, page Pièces) affiche ses variables ; leur modification à la main se fait en Maintenance, avec le niveau d'utilisateur requis.
- **Validation** (à ajouter) : variable de mover utilisée par le cycle de fond ; type incompatible ; valeur lue sur un chemin où elle n'a pas été écrite (même avertissement que pour les variables nommées, sauf pour une variable à règle « jamais » qui garde par construction une valeur utile).

## 6. À préciser plus tard

1. **Remise à zéro « jamais » et changement de type** : si le type d'une variable « jamais » change au déploiement, la valeur est perdue (avec confirmation). À valider quand on l'implémente.
2. **Étape « Réinitialiser » et « Vider le mover »** : à quel niveau d'utilisateur ? Même droit que les actions manuelles, sauf avis contraire.

## 7. Correction du 2026-10-17

Les fonctions du catalogue n'ont presque aucun résultat (une seule en a). Les liaisons sur `FunctionStep` n'apportent donc rien pour l'instant : elles sont reportées avec les sorties des fonctions personnalisées. Par ailleurs l'éditeur n'a aujourd'hui **aucune interface** pour les liaisons de résultats des actions, et son onglet « Données » est un texte figé : l'étape 1 inclut leur création.

## 8. État d'avancement (2026-10-17)

- **Étape 1 réalisée** : variables de process et variables de station nommées, création à la demande depuis les sorties d'une étape, onglet Données, cartes qui affichent « Résultat → variable », validation V020 et V115, annuler / refaire. Décisions D73 à D75. Tests : compilation, validation (analyse d'affectation), persistance, et un test de simulation qui montre qu'une variable de process donne la même production, pièce pour pièce, qu'une variable de station.
- **À faire ensuite** : variables de mover (§ 5), sorties des fonctions personnalisées, sous-process et opérations, objets à sorties variables (Keyence XG).
- **Défauts de clic corrigés au passage** (D71) : les commutateurs `Segmented` et les onglets `Tabs` ne répondaient pas à la souris.
