# Demonware Emulator

Clean .NET rewrite of a private Demonware stack for:

- **Legacy** (3DES): t5 / t6 / iw5 / iw6
- **Modern** (AES + Auth3): iw7 / t7 / t8 / s1 / s2 / h1 / h2

No third-party branding. File-backed storage only (`./data`).

Based on Leaked Cod PDBs and alteriw/s1x/bo3/bo4 Projects

## Ports

| Port | Proto | Service |
|------|-------|---------|
| 3074 | UDP | STUN |
| 3074 | TCP | Modern lobby |
| 3078 | TCP | Legacy lobby |
| 80 (fallback 8080) | HTTP | Auth3 / Umbrella / Uno / Fileshare |

## Layout

```
src/
  Demonware.Core/           shared crypto, TCP, store
  Demonware.Legacy/         3DES lobby protocol
  Demonware.Modern/         AES lobby protocol
  services/
    Demonware.Service.Stun/
    Demonware.Service.Gateway/
    Demonware.Service.LegacyLobby/
    Demonware.Service.ModernLobby/
  Demonware.Host/           runs all microservices in-process
```

Each `Demonware.Service.*` project is a standalone worker. `Demonware.Host` composes them.

## Run

```bash
dotnet run --project src/Demonware.Host
```

Or individually:

```bash
dotnet run --project src/services/Demonware.Service.Stun
dotnet run --project src/services/Demonware.Service.Gateway
dotnet run --project src/services/Demonware.Service.LegacyLobby
dotnet run --project src/services/Demonware.Service.ModernLobby
```

Requires .NET 9+ SDK.