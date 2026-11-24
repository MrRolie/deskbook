# Outils / Toolchain

Rien n'est installé sur la machine : le SDK .NET et PostgreSQL tournent dans les images officielles, avec Docker.
Chaque image est épinglée par son empreinte (digest) ; toute commande utilise l'empreinte, jamais l'étiquette seule.

Nothing is installed on the machine: the .NET SDK and PostgreSQL run in their official images, with Docker. Each
image is pinned by digest; every command uses the digest, never the tag alone.

| Image | Étiquette / tag | Empreinte / digest | Version | Tirée le / pulled |
|---|---|---|---|---|
| Microsoft .NET SDK | `mcr.microsoft.com/dotnet/sdk:10.0` | `sha256:8c6edfb26c85153f236d26efebd9ad349aec403583f343f201ee67838b23658d` | .NET SDK 10.0.404 | 2026-11-23 |
| ASP.NET Core runtime | `mcr.microsoft.com/dotnet/aspnet:10.0` | `sha256:aac50ceef10391e78a899e3d2ccd00bbafa2dcb56ccf4586cbccf00edb84fc21` | .NET and ASP.NET Core 10.0.15 | 2026-11-24 |
| PostgreSQL | `postgres:18` | `sha256:f5cbc081c25690710f3fc6f4c61a7565382bae01ca0d0f2028a53d7926b85a20` | PostgreSQL 18.7 | 2026-11-23 |

L'image d'exécution sert seulement au déploiement (`Dockerfile`) : elle n'a pas de compilateur et tourne sous
l'utilisateur non privilégié `app`. The runtime image is used only for deployment (`Dockerfile`): it has no compiler
and runs as the unprivileged `app` user.

Paquets : nuget.org seulement (`nuget.config` efface toute autre source). Packages: nuget.org only (`nuget.config`
clears every other source).
