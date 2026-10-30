# OPL Companion

OPL Companion is a Puck plugin providing OPL server companion features, including server-browser presentation, message-board and settings tools, competitive skating configuration, zone overlays, and administrator-controlled trophy displays.

## OPL Arena integration

The client build also includes the Sydney/TLAN OPL Arena integration. It is
client-only and activates when the TLAN Arena workshop mod is enabled. The
`OPL ARENA` toggle is available in `Settings > Game`, immediately above
`Show Puck Silhouette`, and is enabled by default.

When enabled, the Companion supplies the Sydney arena, TLAN day/night skybox
and lighting integration, Sydney-positioned floodlights, rectangular ice-light
reflection contribution, goal-dome sirens and period lights, the optional
Locker Room stadium snapshot, and the TRL ice texture handling. When disabled,
the standard TLAN arena behavior remains active and the Companion's OPL arena
effects are disabled; TRL textures remain available in the Locker Room.

Changing the toggle displays `Changes will be reflected next time you enter a
server`. The arena selection is refreshed for the next arena load.

The client output includes `SydneyTLANArena.json` and the `AssetBundles`
directory beside `OPLServerBanners.dll`. The TLAN Arena workshop mod remains a
required dependency. Remove or disable the standalone `SydneyTLANArena` plugin
when using this integrated build so its Harmony hooks and settings UI are not
loaded twice.

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

The client-only OPL Arena files are copied to the same client plugin directory:
`SydneyTLANArena.json` and `AssetBundles/`.

The integrated goalie speed cap is configured in `serverconfig.json`:

```json
"configVersion": 2,
"goalieSpeedCap": {
  "enabled": true,
  "maxSpeed": 9.25
}
```

When an older `serverconfig.json` is loaded, OPL upgrades it in place by adding only missing settings and preserving existing values and unknown properties. The patch is applied on both the dedicated server and client. Remove the standalone `GoalieSpeedCap.dll` before enabling this integrated version so the movement and dash methods are not patched twice.

## Install

For normal local-plugin installation, place these files in
`Puck/Plugins/OPLServerBanners/` on both the client and dedicated server:

```text
OPLServerBanners.dll
serverconfig.json
Assets/Trophies/s4_trophies
```

On the client, also install `SydneyTLANArena.json` and the OPL Arena
`AssetBundles/` directory beside the DLL.

Restart Puck/the dedicated server after updating the plugin. Live servers using the Steam Workshop version must also receive the updated workshop build.

### Steam Workshop client installation

The Workshop package is also a native client code mod. It contains the same
`OPLServerBanners.dll` as the local build, and Puck's Workshop loader discovers
its `IPuckPlugin` entrypoint from the item directory. Subscribe to Workshop
item `3726105987`, enable it in Puck's mod manager, and enable the `OPL ARENA`
option in Settings > Game. The arena bundle, configuration, and runtime
effects are then loaded from the Workshop item directory; no local
`Puck/Plugins/OPLServerBanners` copy is required for the client.

If both a local copy and the Workshop copy are present, Companion detects the
second assembly path and makes that instance a no-op, preventing duplicate
Harmony patches and duplicate arena effects.

## Assets

This repository contains the built `assets/trophies/s4_trophies` and
`assets/opl-arena/AssetBundles/oplsydney` runtime bundles. Trophy FBX files,
the Unity authoring project, and legacy warmup-obstacle source and bundles are
intentionally excluded.

## Repository layout

```text
src/Core/             Plugin bootstrap and shared server helpers
src/Compatibility/    Optional third-party mod compatibility
src/Features/         Source grouped by OPL Companion feature
assets/trophies/      Runtime trophy AssetBundle
assets/opl-arena/     Runtime Sydney arena AssetBundles
config/               Default server and message-board configuration
docs/                 Feature documentation and reference material
examples/             Configuration examples
tools/                Maintainer utilities
```
