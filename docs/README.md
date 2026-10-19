# Documentation du standard de machine

Lire dans cet ordre. Chaque document fait foi dans son domaine ; en cas de contradiction, le plus spécialisé l'emporte (par exemple `05` sur `02` pour un algorithme), et la contradiction doit être signalée.

| Document | Contenu | Pour |
| --- | --- | --- |
| `00-glossaire.md` | Sens de chaque mot, noms de code, mots à éviter | Tous |
| `01-definition-machine.md` | Ce qu'est la machine, fonctions requises, suivi des pièces, machine d'exemple MX-4 | Tous |
| `02-specifications-fonctionnelles.md` | Niveaux d'abstraction, configuration, process, PackML, fonctions et Aborted, routage, manuel, pas à pas, overrides, alarmes, OF, droits | Tous |
| `03-catalogue-objets.md` | Tous les types d'objets et de modules : paramètres, signaux, actions, résultats, erreurs, simulation | HMI et PLC |
| `04-plc-architecture-objets.md` | Architecture de l'objet élémentaire dans TwinCAT : FB de base, contrat d'action, E/S, override, timeouts, pools | PLC (et simulateur) |
| `05-plc-sequenceurs.md` | Algorithmes : PackML, interpréteur, séquenceur de station, séquenceur général, movers, manuel, pas à pas, déploiement | PLC et simulateur |
| `06-tables-et-interface-hmi-plc.md` | Jeu d'instructions, structures déployées, compilation des process, symboles ADS, `IPlcClient` | HMI et PLC |
| `07-hmi-architecture.md` | Décisions techniques de la HMI, solution, modèle, compilateur, validation, persistance, tactile, simulateur, tests | HMI |
| `08-hmi-design-ecrans.md` | Design system, coquille, navigation, chaque écran, retours visuels en fonctionnement | HMI |
| `09-journal-des-decisions.md` | Décisions prises et leur raison, à compléter au fil du développement | Tous |
| `10-surveillance-des-rebuts.md` | Rebuts successifs et taux par mover, station, fonction, machine, OF : seuils, réactions, libération, imputation, algorithme PLC, écrans | Tous |
| `11-interface-usine.md` | Remontée vers le système de l'usine : données, OPC UA (PackTags), MQTT et outbox, OF entrants, sécurité ; première version limitée aux interfaces et aux réglages | Tous |
| `12-forcages-et-etats-declares.md` | États déclarés (dont démarrage bloqué), station hors production, mover hors service, forçages d'essai en simulation, page Forçages | Tous |
| `ui-reference/` | Maquette Machine Studio (Claude Design) : référence du langage visuel et de l'usage de l'éditeur de process | HMI |

État : spécification complète pour le premier aperçu (HMI + simulateur). Le code PLC réel (bibliothèque `StdMachineLib`, client ADS) vient ensuite, sur la base des documents 04 à 06.

Études en cours (sans valeur de référence tant qu'elles ne sont pas validées et reportées dans les documents) :

| Document | Contenu |
| --- | --- |
| `etudes/table-tournante-weiss.md` | Adapter le standard à une table tournante à indexage (Weiss) à la place du système planaire, avec sorties de pièces hors de la table |
