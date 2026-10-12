# OPL Companion

OPL Companion is a Puck plugin providing OPL server companion features, including server-browser presentation, message-board and settings tools, competitive skating configuration, zone overlays, and administrator-controlled trophy displays.

## Trophy commands

Only Steam IDs listed by the Puck server's `admin_steam_ids.json` can use these commands:

| Command | Behaviour |
| --- | --- |
| `/wbw cup` | Spawn or despawn the WBW Cup at centre ice. |
| `/bi cup` | Spawn or despawn the BI Cup at centre ice. |

The active cup remains visible through game-phase changes until it is toggled off. Cups are visual-only: they have no collider, Rigidbody, or server-side rendered object.

## Build

Requirements:

- Puck installed locally.
- .NET SDK with .NET Framework 4.8 targeting support.
- The Puck/DomaWaterBottle reference assemblies available at the relative path used by `OPLServerBanners.csproj`.

Build the production configuration:

```powershell
dotnet build OPLServerBanners.csproj -c TrophyCommands
```

The project copies the DLL and `assets/trophies/s4_trophies` bundle to the configured Puck client and dedicated-server plugin folders. Those absolute destinations are defined in `OPLServerBanners.csproj`; update them for a different Puck installation.

## Install

Place these files in `Puck/Plugins/OPLServerBanners/` on both the client and dedicated server:

```text
OPLServerBanners.dll
serverconfig.json
Assets/Trophies/s4_trophies
```

Restart Puck/the dedicated server after updating the plugin. Live servers using the Steam Workshop version must also receive the updated workshop build.

## Assets

This repository contains only the built `assets/trophies/s4_trophies` AssetBundle needed at runtime. Trophy FBX files, the Unity authoring project, and legacy warmup-obstacle source and bundles are intentionally excluded.

## Repository layout

```text
src/Core/             Plugin bootstrap and shared server helpers
src/Compatibility/    Optional third-party mod compatibility
src/Features/         Source grouped by OPL Companion feature
assets/trophies/      Runtime trophy AssetBundle
config/               Default server and message-board configuration
docs/                 Feature documentation and reference material
examples/             Configuration examples
tools/                Maintainer utilities
```
