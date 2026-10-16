# Reprise : phases 2 à 5 (session distante, sans Windows)

Dernière mise à jour : 2026-10-16. Branche `develop`. Tout ce qui est décrit ici est dans les projets `Core`, `Persistence` et `Simulation` et est couvert par des tests (`dotnet test StdMachine.sln` : 7 + 53 + 81 tests verts, sous .NET 8 sur Linux). **Rien n'a été compilé en WPF** : le branchement de la HMI (`--simulator`) est à vérifier sur ton poste.

## À faire en premier sur le poste Windows
1. `powershell -File tools\build.ps1` : doit compiler les 3 nouveaux projets et la HMI (nouvelles références : `StdMachine.Persistence`, paquet `System.Text.Json` via `Core`). En cas d'erreur de restauration NuGet : voir `POC-NET48.md`.
2. Tests : `dotnet test` n'existe pas sur le poste (pas de SDK) ; ouvrir la solution dans Visual Studio, Explorateur de tests (xunit, net48). Les projets de tests visent net48 sous Windows.
3. Lancer `src\StdMachine.Hmi\bin\Debug\net48\StdMachine.Hmi.exe --simulator --windowed` : la machine démarre arrêtée ; Réarmer puis Démarrer dans la barre du haut.
4. Captures : `StdMachine.Hmi.exe --simulator --scenario running --screenshot-all artifacts\screens` (un dossier `sim-<scénario>` par scénario).

## Ce qui est fait
- **Phase 2** : `catalog/catalog.json` (28 types, 2 modules), modèle complété (E/S, matériel, recettes de presse, verrouillages, qualité), `samples/MX-4.stdm.json` (produit par `SampleProject`, régénéré avec `STDM_UPDATE_SAMPLES=1` sous les tests de persistance), JSON versionné avec migrations, validation V001 à V113.
- **Phase 3** : `ProjectCompiler` → `CompiledTables` (cartes, blocs, fonctions de module dépliées, conditions, textes, variables cachées, verrouillages générés, carte de compilation, listing `Listing.Of`, empreinte SHA-256, compilation de MX-4 en quelques ms).
- **Phase 4** : `MachineSim` (PackML machine et stations, interpréteur à contextes, objets simulés, séquenceur général, routage, movers, pièces, traces, OF, Hold/Stop/Abort/Clear, station hors production, mover hors service, états déclarés), surveillance des rebuts (`QualityMonitor`), pas à pas, canal manuel, scénarios.
- **Phase 5 (partielle)** : `SimulatedPlcClient` publie les instantanés ; `--simulator` branche la HMI dessus.

## Écarts et reste à faire (voir aussi `docs/09`, D49 à D63)
- Non implémentés : V014, V106, V114, V201 ; SQLite (utilisateurs, OF persistants, journal) ; services d'exploitation (AlarmService, TraceService en base) ; courbes de presse ; sécurités simulées (`SetSafetyCircuit`) ; collisions et trajets du planaire (lignes droites interpolées).
- La HMI affiche encore des données de démonstration dans plusieurs pages (Studio, Déploiement, Utilisateurs, Communications, Système) ; le Studio édite le projet en mémoire et `Déployer` appelle `SimulatedPlcClient.Deploy` (machine à l'arrêt).
- `PlcCommands`, pages Manuel et Forçages : utilisent déjà `IPlcClient` ; leur comportement sur le simulateur n'est pas vérifié visuellement.
- Phase 6 (Studio complet), 7 (Manuel, pas à pas, forçages en pages réelles) et 8 (utilisateurs, droits, communications) : non commencées.
