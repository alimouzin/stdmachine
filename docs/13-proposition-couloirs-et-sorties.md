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

### Proposition : un seul mécanisme, la signature de sorties

Une **sortie** est un nom, un type (nombre, entier, booléen, position, jugement, texte) et une unité. Elle est déclarée là où elle a un sens, puis **utilisable par les étapes suivantes sans déclarer de variable**.

**a) Valeur d'étape (le cœur).** Chaque sortie d'une étape devient automatiquement une source dans les sélecteurs de valeur des étapes qui suivent (« Position de la pièce · étape 3 »). Le compilateur lui attribue une variable cachée (il en alloue déjà pour les compteurs de boucle). Pour garder une valeur d'un process à l'autre ou la mémoriser, on la lie, comme aujourd'hui, à une variable de station ou à une donnée de pièce.

- Portée : les étapes qui suivent dans le même process, y compris dans les blocs imbriqués ; pas dans une autre branche parallèle (avant la jointure) ; pas vers l'autre branche d'un Si / Sinon.
- Validation (nouvelle règle, dans l'esprit de V014) : usage d'une sortie qui n'est pas garantie d'avoir été produite (étape dans une branche Si non prise, ou après une politique d'échec « Ignorer »), type incompatible.
- Pas de changement du jeu d'instructions du PLC (`docs/06`) : la valeur d'étape se compile en variable ordinaire.

**b) Blocs composés avec sorties.**

- **Fonction personnalisée** (dépliée à la compilation) : une sortie est un résultat d'une de ses actions internes, relié à une « sortie de la fonction ». Au point d'appel, le compilateur remplace par la cible de l'appelant : aucun coût d'exécution.
- **Sous-process et opération** (appelés, pas dépliés) : la sortie est écrite dans une variable cachée du bloc, copiée vers la cible de l'appelant juste après le retour (une instruction de copie). Nouvelle règle : un bloc à sorties ne peut pas être appelé en même temps depuis deux contextes (deux branches parallèles, ou opération et cycle de fond).
- **Fonctions de module du catalogue** : ajouter les liaisons de résultats à `FunctionStep` (même mécanisme).
- Exemple visé : un bloc « Rechercher pièce Asyril » = chercher (refaire 3 fois avec vibration), puis obtenir ; sortie « Position pièce » (Position) et « Nombre » (Entier).

**c) Objets à sorties variables (Keyence XG).** Un type du catalogue porte l'indicateur `variableOutputs` sur son action de lecture (« Lire résultats »). L'objet, dans le Studio station, a une table « Sorties » : nom, type, unité, plage de simulation, emplacement source (opaque pour l'instant). Les résultats de l'action sont alors la liste de l'instance. Le simulateur génère des valeurs selon la plage ; le protocole réel (EtherNet/IP ou TCP, décalages) se règle en phase 9, sans changer la HMI.

### Limites connues

- Le PLC garde **8 résultats** par action (`ST_ActResults` : `aReal[8]`, un seul `stPos`, un seul `sText`). Une caméra à plus de 8 valeurs, ou à deux positions, demande d'élargir cette structure (changement de `docs/04` et `docs/06`).
- Variables : visibles + cachées ≤ 64 par station. Des valeurs d'étape nombreuses l'épuiseraient. Première version : allocation simple et règle de validation claire. Évolution : réutiliser une variable cachée après sa dernière lecture.

### Ordre de réalisation proposé

1. **Valeurs d'étape sur les résultats existants** (Asyril position, Keyence IV jugement et valeur) + liaisons sur `FunctionStep` + sélecteurs de l'éditeur + règles de validation. Démontrable de bout en bout avec le simulateur : « Obtenir pièce » → « Prendre pièce » sans variable déclarée.
2. **Sorties des fonctions personnalisées**, puis des sous-process et opérations.
3. **Objets à sorties variables** (type Keyence XG, table de sorties, simulation).

Chaque étape : tests de compilation (allocation, portée), tests de validation, test de simulation, captures regardées, journal de décisions.

### Documents touchés (après accord)

`docs/00` (mots « Sortie », « Valeur d'étape »), `docs/02` § 5.2 et 5.3, `docs/03` (indicateur `variableOutputs`, type XG), `docs/04` (seulement si on dépasse 8 résultats), `docs/06` § 5 (compilation), `docs/07` § 3.2 (modèle) et § 4.2 (règles), `docs/08` § 6.9 (panneau Étape, sélecteurs), `docs/09` (décisions).

## 3. Décisions du 2026-10-17

1. **« Bloc »** = une carte d'étape (action, fonction, sous-process, opération). Les blocs de structure (Si / Sinon, En parallèle, Répéter) n'ont pas de sortie propre. **Décidé.**
2. **Valeurs d'étape : les deux.** Une sortie est utilisable directement par les étapes suivantes (variable cachée allouée par le compilateur) **et** peut être liée explicitement à une variable de station ou à une donnée de pièce, pour la garder d'un process à l'autre. **Décidé.**
3. **Plusieurs sorties par bloc, nommées ou non.** Une sortie sans nom se désigne par son rang (« Sortie 1 », « Sortie 2 »…) ; une sortie nommée garde son nom partout (cartes, sélecteurs, liaisons). Le Keyence XG est remis à plus tard : l'étape 3 de l'ordre de réalisation attend. **Décidé.**
4. **Couloirs : prototype du mode « Flux ».** Fait, voir ci-dessous. Le choix définitif reste à faire.

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
