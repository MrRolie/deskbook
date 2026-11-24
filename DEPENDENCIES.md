# Dépendances / Dependencies

Toutes viennent de nuget.org (`nuget.config` n'autorise aucune autre source). Versions exactes dans les `.csproj`.
All come from nuget.org (`nuget.config` allows no other source). Exact versions are in the `.csproj` files.

## Le programme / The program

| Paquet / Package | Version | Pour quoi / What it is for | Sans lui / Doing without it |
|---|---|---|---|
| `Npgsql` | 11.0.0 | Le pilote PostgreSQL pour .NET : connexions, paramètres, transactions, types `date` et `numeric` vers `DateOnly` et `decimal`. / The PostgreSQL driver for .NET: connections, parameters, transactions, `date` and `numeric` mapped to `DateOnly` and `decimal`. | Réécrire le protocole réseau de PostgreSQL : aucune solution raisonnable. / Rewriting PostgreSQL's wire protocol: no reasonable alternative. |

Npgsql apporte `Microsoft.Extensions.Logging.Abstractions`, `Microsoft.Extensions.DependencyInjection.Abstractions`
et `System.Diagnostics.DiagnosticSource` (11.0.0, de Microsoft). Npgsql brings these three Microsoft packages.

Tout le reste vient de la bibliothèque standard de .NET : `System.Text.Json` pour le JSON, `HttpClient` pour l'API
Valet, `TimeProvider` pour l'horloge. Pas d'ORM : le SQL est écrit à la main, visible et court. Pas de bibliothèque
de ligne de commande : sept commandes ne la justifient pas.

Everything else is .NET's standard library: `System.Text.Json` for JSON, `HttpClient` for the Valet API,
`TimeProvider` for the clock. No ORM: the SQL is hand-written, visible and short. No command-line library: seven
commands do not justify one.

## Les tests seulement / Tests only

| Paquet / Package | Version | Pour quoi / What it is for | Sans lui / Doing without it |
|---|---|---|---|
| `xunit` | 2.9.3 | Le cadre de test : `[Fact]`, `[Theory]`, `Assert`, les fixtures partagées. / The test framework: `[Fact]`, `[Theory]`, `Assert`, shared fixtures. | MSTest ou NUnit (autres paquets), ou un exécutable maison d'assertions, sans l'outillage. / MSTest or NUnit (other packages), or a home-made assertion runner, without the tooling. |
| `xunit.runner.visualstudio` | 3.1.4 | Permet à `dotnet test` de trouver et lancer les tests xUnit. / Lets `dotnet test` find and run xUnit tests. | Lancer les tests par la console d'xUnit. / Run the tests through xUnit's own console. |
| `Microsoft.NET.Test.Sdk` | 17.14.1 | L'hôte de test de `dotnet test`. / The test host behind `dotnet test`. | Idem : pas de `dotnet test`. / Same: no `dotnet test`. |

Ces paquets apportent leurs propres dépendances (`xunit.core`, `xunit.assert`, `Microsoft.TestPlatform.*`,
`Newtonsoft.Json` côté hôte de test) ; aucune n'est livrée avec le programme. These bring their own dependencies;
none ships with the program.
