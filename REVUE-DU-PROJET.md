# Revue du projet

Faite le 2026-10-18 sur la branche `feature/verification-simulateur`, sans compilateur. Relu : tous les documents de `docs/` (00 à 13), les fichiers de la racine, la structure du code (5 projets, 24 000 lignes), l'interface PLC (`PlcClient.cs`), le validateur, les services de la HMI, la persistance, le simulateur et la liste des 114 tests. Les numéros de code et de ligne citent l'état du commit `0654fdf`.

Trois parties : ce que j'ai corrigé pendant la revue, ce qui manque dans le code par rapport aux documents (pour l'agent qui code), et ce qui manque dans la spécification elle-même (à décider).

## 1. Corrigé pendant la revue (décision D85)

- `docs/02` § 16 : une ancienne puce sur la fin d'OF contredisait le changement d'OF à chaud (D83) ; retirée.
- `docs/05` § 3.3 : la table de l'interpréteur ne connaissait pas les instructions ajoutées (`WaitMover`, `ResetMoverVars`, `Prompt`, `SavePos`, `CalibStation`) ; complétée.
- `docs/05` § 3.4 et § 3.5, `docs/06` § 2 : source `OrderParam` absente de la résolution des paramètres ; cible de liaison « sortie de bloc » utilisée par `docs/07` mais absente de `E_BindTarget` (ajout `Output := 7`) ; la variable système « n° d'OF » désigne désormais l'OF de la pièce.
- `docs/05` § 11 : limites `MAX_ORDERS` (8) et `MAX_OUTPUTS` (8) ajoutées.
- `docs/06` § 7 : l'interface `IPlcClient` documentée était en retard sur le code (commandes de station, forçages, qualité) et sur les ajouts (fin des pièces, OF à chaud, mover en manuel, variables de mover, réponse à une demande de calibration) ; alignée.
- `docs/07` § 10 et § 12 : scénarios de capture (`draining`, `orders`, `calibration`) et tests pour la fin des pièces, la préparation, les OF, les calibrations, et des compléments de routage qui n'étaient testés nulle part (priorité de file, mover à vider, traces sans perte).
- `docs/11` § 2 : « OF en cours » devient « OF actif et OF suspendus ».
- **Décision prise en attendant votre avis** : un mover **hors service** peut être absent du plan au Réarmer. Avant, `docs/05` § 5.2 exigeait « tous les movers détectés » : un mover parti en réparation empêchait de redémarrer la machine.

## 2. Manques du code par rapport aux documents

Pour l'agent qui code. En plus des chantiers spécifiés et pas encore codés (D77 à D84, ordre dans `REPARTITION-DES-TRAVAUX.md`).

### Priorité 1 : bloquant pour un usage réel

1. **Le projet n'est jamais enregistré.** `ProjectService` (`src/StdMachine.Hmi/Services/AppServices.cs:31`) recharge `samples/MX-4.stdm.json` à chaque démarrage et garde les modifications en mémoire : tout ce qui est fait dans le Studio est perdu à la fermeture. Il manque l'enregistrement automatique (2 s, `docs/07` § 5.1), ouvrir, importer, exporter, la copie `deployed.stdm.json` et l'historique des déploiements avec restauration. `ProjectSerializer.Save` existe déjà.
2. **Aucune base de données** (SQLite, `docs/07` § 5.2, exclue du PoC .NET 4.8) : pas d'historique des alarmes, des pièces et des traces, des OF, du journal, des arrêts ni des forçages. La traçabilité et le journal disparaissent à la fermeture, et les forçages ne survivent pas à un redémarrage, contrairement à `docs/12` § 1. Le paquet `Microsoft.Data.Sqlite` demande votre accord.
3. **Utilisateurs et droits** : la connexion accepte tout PIN de 4 chiffres (`LoginPanel.xaml.cs:8`) et aucun droit n'est appliqué. Le code de `Core/Security` est prêt et testé.
4. **Les paramètres d'OF ne vont pas au PLC** : `OrderStart` ne porte que numéro, référence et quantité (`PlcClient.cs:54`). Les recettes viennent aujourd'hui de constantes du process général.

### Priorité 2

5. Textes de sous-alarmes (`SubAlarms_*.xml`, `docs/02` § 14) non chargés ; le dossier `catalog/subalarms/` n'existe pas.
6. Règles de validation non codées : V014 (le modèle des liaisons existe depuis D75, elle est donc possible), V106, V114, V201 ; plus les nouvelles règles V021 à V031 et V116 à V121.
7. Apprentissage d'une position non branché sur `TeachPositionAsync` (le simulateur le sait, la HMI ne l'appelle pas).
8. Déplacement manuel d'un mover (`docs/05` § 6.5) absent du client et de la HMI.
9. Déploiement : détail des différences et restauration d'une version précédente (aujourd'hui seulement l'empreinte, D68).
10. Export des constantes PLC `GVL_Catalog.TcGVL` (page Système) absent.
11. `IPlcClient` n'a pas les flux sans perte d'alarmes, de traces et de courbes (`ReadAlarmEventsAsync`, `ReadTracesAsync`, `ReadCurveAsync`, `docs/06` § 7) : indispensables pour le futur client ADS.
12. `ISimulatorControl` partiel (seulement l'échelle de temps et l'Abort de station dans `Core`) : les forçages d'essai de `docs/12` § 5 passent par une interface propre au simulateur.

### Priorité 3

13. Pages encore en données de démonstration : Utilisateurs, Communications (réglages), Système, historique de la page Station.
14. Mini-carte de l'éditeur en suivi d'exécution (`docs/08` § 7.2) absente.
15. Tests absents : priorité de file d'une station partagée, mover à vider après 60 s, courbes de presse, traces sans trou ; 43 avertissements `xUnit1031`.

### Plateforme

Le code est en .NET Framework 4.8 (PoC, D41) alors que la cible est .NET 10 (D20) avec `Microsoft.Extensions.Hosting`, Serilog et SQLite. L'agent Windows a un environnement complet : c'est le bon moment pour décider la migration, **avant** d'écrire la persistance et la base, qui en dépendent.

## 3. Manques de la spécification

Classés par importance, avec ma recommandation. Rien n'est écrit dans les documents tant que vous n'avez pas choisi.

1. **Repères (frames) et transformations.** Une position porte un n° de frame, la vision Asyril rend une position dans son frame (`docs/03` § 6.3, P2), mais aucun document ne définit les frames ni la transformation vers le repère du robot : un axe prend sa composante sans tenir compte du frame. Une prise sur vision ne peut donc pas fonctionner sur une vraie machine. C'est un point ouvert de `docs/02` § 19. *Recommandation* : une table de frames par station (origine X, Y, Z, C par rapport au frame du robot), appliquée à la résolution des paramètres, et calculée par une calibration (D79).
2. **Valeurs qui ne vivent que dans le PLC.** Variables persistantes de calibration, variables de mover « Jamais », compteurs de maintenance : si le PLC est remplacé ou réinstallé, elles sont perdues (les positions apprises, elles, sont aussi dans le projet). *Recommandation* : la HMI en garde une copie (relue après chaque calibration et chaque heure) et propose de les restaurer au déploiement suivant, avec confirmation.
3. **Compatibilité entre la HMI et la bibliothèque PLC.** Aucun contrôle de version au déploiement : un projet qui utilise les instructions 17 à 23 sur une bibliothèque PLC plus ancienne ne serait pas refusé proprement. *Recommandation* : le PLC publie la version de sa bibliothèque et celle du catalogue ; le chargeur refuse un projet incompatible et la HMI le dit. Coût faible.
4. **Maintenance préventive.** Les compteurs existent (actions, erreurs, heures de mouvement par objet, `docs/04` § 5) et remontent à l'usine, mais il n'y a ni seuil, ni alerte, ni écran. *Recommandation* : seuils par objet, avertissement « maintenance à prévoir », onglet Maintenance, remise à zéro avec motif et journal.
5. **Détection d'un blocage de la machine.** Seul le cas « pas de station de rebut pendant 60 s » est traité. *Recommandation* : en Execute, une alarme si aucun mover n'a progressé depuis N secondes, avec le diagnostic (qui attend quoi).
6. **Références produit.** Les paramètres d'un OF se ressaisissent à chaque OF. *Recommandation* : une table des références (paramètres par défaut, recettes, budget de rebuts) ; un OF se crée en choisissant la référence, et ses paramètres sont vérifiés (recette de presse existante…).
7. **Traçabilité des composants (lots).** Rien ne permet de rattacher le lot des pignons ou des platines aux pièces. *Recommandation* : un « lot en cours » déclaré par alimenteur ou magasin (saisi ou lu), copié dans une donnée de pièce par l'opération.
8. **Contourner une fonction.** Si la station Contrôle est en panne, les movers attendent. *Option* : contourner une fonction (droit, motif, pièces marquées « non contrôlées »). Risque qualité, à n'ouvrir que si un client le demande.
9. **Cycle à vide** (movers et stations sans pièce, pour roder après une intervention) : couvert en partie par l'état déclaré Essais et les objets en simulation ; à préciser si besoin.
10. **Aide aux alarmes** : cause probable et remède par code d'erreur, lien vers la documentation.
11. **Plusieurs postes HMI ou accès à distance** : non spécifié (qui peut commander, poste en lecture seule ?). À décider avant le client ADS.
12. **Horloge** : synchronisation de l'heure du PLC et des postes, fuseau horaire des traces ; seul NTP pour les postes est cité (`docs/11` § 4.2).
13. **Dossier machine** : export de la configuration (liste des E/S, objets, positions, listing des process) pour la documentation de la machine.
14. **Changement de série guidé** : enchaîner Finir les pièces, état déclaré Changement de série, changement d'outillage, choix de l'OF, Démarrer.

## 4. Questions à trancher

> **Réponses du 2026-10-18** : frames gérés seulement par les robots Stäubli, qui font toutes les transformations (D86, le point 3.1 est fermé) ; questions 1 à 3 acceptées (D87, D88) ; ordre de spécification accepté : compatibilité (D89) puis sauvegarde des valeurs du PLC (D90), faits. Restent ouvertes : la question 4 (couloirs) et les points 3.4 à 3.14.

1. **Fenêtres qualité et changement d'OF** (`docs/10` § 4.2) : les fenêtres de rebut sont remises à zéro quand la référence produit change. Avec le changement d'OF à chaud, alterner deux références les remet à zéro à chaque bascule, alors que des pièces de l'ancienne référence sont encore en cours. Le plus simple : remise à zéro à l'activation d'un OF d'une autre référence, les dernières pièces de l'ancienne comptant dans la nouvelle fenêtre. Autre choix, plus lourd : une fenêtre par référence.
2. Mover hors service absent au Réarmer (D85) : la décision vous convient-elle ?
3. Migration vers .NET 10 maintenant, et accord pour `Microsoft.Data.Sqlite` ?
4. Couloirs de l'éditeur : à garder en option ou à supprimer (en attente depuis D72).
5. Parmi les manques de la partie 3, lesquels spécifier, et dans quel ordre ? Ma proposition : 1, 3 et 2 d'abord (ils conditionnent une vraie machine), puis 4 et 5.
