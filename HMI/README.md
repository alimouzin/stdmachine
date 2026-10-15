# HMI (aperçu)

Solution C# de la HMI : `src/Core` (modèles, machine à états, droits, OF, simulateur PLC, sans WPF), `src/Hmi` (WPF, MVVM), `tests/Core.Tests`.

```
dotnet build StdMachine.sln
dotnet test StdMachine.sln
dotnet run --project src/Hmi
```

La machine est déclarée dans `src/Core/machine.json` (copié à côté de l'exécutable). Utilisateurs et droits sont enregistrés dans `%AppData%\StdMachine\access.json`, les OF dans `orders.json`.

Utilisateurs par défaut (nom / PIN) : Opérateur 1111, Régleur 2222, Process 3333, Admin 0000.
