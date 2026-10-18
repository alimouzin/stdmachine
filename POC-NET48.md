> **N'est plus applicable depuis le 2026-10-18 (D88)** : le projet migre vers .NET 10, avec `Microsoft.Extensions.Hosting`, Serilog et `Microsoft.Data.Sqlite` (`docs/07` § 1). Ce fichier est gardé comme historique.

# Addendum PoC : cible .NET Framework 4.8

Ce fichier s'applique **uniquement au PoC de la phase 1** sur un poste sans droits administrateur, sans accès à dot.net, sans SDK .NET 10, avec Visual Studio 2022 Community. Il s'ajoute à `CLAUDE.md` et `PROMPT.md` et remplace seulement les points listés ci-dessous. Le reste s'applique tel quel.

La cible standard reste **.NET 10** (décision D20). Ce PoC ne la remet pas en cause : il sert à valider le concept, pas à figer la plateforme.

## Décisions pour ce PoC

- **Cible** : `net48`, projets au format SDK (`<TargetFramework>net48</TargetFramework>`, `<UseWPF>true</UseWPF>`, `<LangVersion>latest</LangVersion>`).
- **Syntaxe** : C# récent accepté, mais seulement ce qui compile sur net48. Les types manquants du framework (`IsExternalInit` pour `init`, `RequiredMemberAttribute`, `CollectionBuilderAttribute`, etc.) se déclarent dans un seul fichier `src/StdMachine.Hmi/Compatibility.cs`, sans dépendance.
- **Outils de compilation** : `dotnet` n'est pas disponible. Utiliser MSBuild de Visual Studio :
  `C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe`
  Restauration des paquets : `msbuild /t:Restore`, puis `msbuild /p:Configuration=Debug`.
- **Paquets** : seul `CommunityToolkit.Mvvm` est autorisé, dans une version qui se restaure pour net48 (à vérifier au restore). Tout autre paquet : s'arrêter et demander.
- **Hors périmètre du PoC** : pas de SQLite, pas d'ADS, pas de `Microsoft.Extensions.Hosting` (le conteneur de services se construit à la main dans `App.xaml.cs`), pas de Serilog (journal minimal écrit dans un fichier ou la sortie de débogage), pas de génération de source `System.Text.Json`, pas de projet de tests.
- **Inchangés** : design, tokens, polices (IBM Plex Sans, Material Symbols), composants, `DragDropService`, pavé numérique et clavier intégrés, `DemoPlcClient`, scénario de démonstration, captures d'écran.
- **Journal** : ajouter une ligne **D41** à `docs/09-journal-des-decisions.md` : « PoC de la phase 1 en .NET Framework 4.8, sur poste sans SDK .NET 10 ni droits administrateur ; la cible standard reste .NET 10 (D20) ». Ne modifier ni `PROMPT.md` ni `docs/07`.

## Vérifications au démarrage (avant le plan)

1. MSBuild existe à l'emplacement ci-dessus, et `msbuild -version` répond.
2. Le pack de ciblage .NET Framework 4.8 est installé : le dossier `C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8` doit exister. Sinon, la charge de travail « Développement de bureau .NET » manque dans Visual Studio : le signaler et s'arrêter.
3. `nuget.org` répond (`api.nuget.org` est accessible).

Si l'une de ces vérifications échoue, ne pas contourner : le signaler dans le plan et attendre.

## Points à rapporter après le build

- Les écarts réels avec la cible .NET 10 (syntaxe, paquets, shims ajoutés), pour qu'ils soient connus si la version standard est reprise plus tard.
- Le temps de démarrage et la fluidité des animations : ce sont justement les critères du point de validation.
