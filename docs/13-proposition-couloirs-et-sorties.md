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

## 3. Questions à trancher

1. **« Bloc »** : j'ai compris une carte d'étape (action, fonction, sous-process, opération). Les blocs de structure (Si / Sinon, En parallèle, Répéter) n'ont pas de sortie propre : ils écrivent des variables par « Définir variable ». Est-ce bien ce que vous voulez ?
2. **Valeurs d'étape automatiques** (pas de variable à déclarer pour une valeur qui ne sert qu'à l'étape suivante), ou liaison explicite à une variable à chaque fois, comme aujourd'hui ?
3. **Keyence XG** : combien de valeurs, de quels types, et comment sont-elles fournies (signaux, bloc de données réseau) ? Cela ne conditionne que l'étape 3 ci-dessus.
4. **Couloirs** : je prototype le mode « Flux » en second mode pour comparer sur votre écran, ou vous préférez décider tout de suite de les retirer ?
