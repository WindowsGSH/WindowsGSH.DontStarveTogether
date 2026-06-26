# Dont Starve Together Dedicated Server

WindowsGSH module for Don't Starve Together dedicated servers.

## Support

If this module helps you host your servers, you can support development here:

- [Ko-fi](https://ko-fi.com/shenniko)
- [PayPal](https://paypal.me/shenniko)

## Module Layout

```text
WindowsGSH.DontStarveTogether/
  README.md
  LICENSE.md
  DontStarveTogether.mod/
    module.json
    DontStarveTogetherModule.cs
```

Import `DontStarveTogether.mod` directly, or import the repository root and let WindowsGSH discover the nested module folder.

## Current Status

- Installs through SteamCMD app `343050`.
- Starts `bin64/dontstarve_dedicated_server_nullrenderer_x64.exe` when available, with x86 fallback.
- Uses an isolated Klei config root under the server files folder by default.
- Writes the modern cluster layout: `cluster.ini`, `cluster_token.txt`, `Master/server.ini`, `Master/worldgenoverride.lua`, `Caves/server.ini`, and `Caves/worldgenoverride.lua`.
- Starts the Master shard as the main process and, when enabled, starts the Caves shard as a second process.
- Writes `mods/dedicated_server_mods_setup.lua` and cluster `modoverrides.lua`.
- Uses process status for query checks.
- Supports console commands through redirected stdin.

## Important Settings

- `server.token`: Klei cluster token. WindowsGSH writes this to `cluster_token.txt`; it should be treated as secret.
- `profile.root`: profile storage root relative to the server files folder. Defaults to `KleiConfig`.
- `profile.confDir`: Klei configuration directory. Defaults to `DoNotStarveTogether`.
- `cluster.name`: cluster folder/name passed to DST with `-cluster`. Defaults to `MyDediServer`.
- `shard.enableCaves`: starts a second process using `-shard Caves`.
- `network.port`: Master shard game port.
- `network.cavesPort`: Caves shard game port.
- `network.clusterMasterPort`: internal shard communication port written to `cluster.ini`.
- `network.masterServerPort` / `network.authenticationPort`: Steam ports for the Master process.
- `network.cavesMasterServerPort` / `network.cavesAuthenticationPort`: Steam ports for the Caves process.
- `server.disableDataCollection`: leave disabled for public servers. When enabled, WindowsGSH adds `-disabledatacollection`; testing showed that can prevent the server from appearing in the in-game server list.
- `mods.workshopIds`: Workshop IDs, one per line or separated by commas/spaces.
- `mods.overridesLua`: optional complete `modoverrides.lua` content. If blank, WindowsGSH enables every ID from `mods.workshopIds`.
- `worldgen.masterPreset`: preset used for `Master/worldgenoverride.lua`. Advanced Lua can be edited after install.
- `worldgen.cavesPreset`: preset used for `Caves/worldgenoverride.lua`. Defaults to `DST_CAVE`.

## Configuration Files

The module writes:

```text
KleiConfig/DoNotStarveTogether/<cluster>/cluster.ini
KleiConfig/DoNotStarveTogether/<cluster>/cluster_token.txt
KleiConfig/DoNotStarveTogether/<cluster>/modoverrides.lua
KleiConfig/DoNotStarveTogether/<cluster>/Master/server.ini
KleiConfig/DoNotStarveTogether/<cluster>/Master/worldgenoverride.lua
KleiConfig/DoNotStarveTogether/<cluster>/Caves/server.ini
KleiConfig/DoNotStarveTogether/<cluster>/Caves/worldgenoverride.lua
mods/dedicated_server_mods_setup.lua
```

The launch arguments resolve these files with:

```text
-console -persistent_storage_root "<server files>/KleiConfig" -conf_dir DoNotStarveTogether -cluster <cluster> -shard Master
-console -persistent_storage_root "<server files>/KleiConfig" -conf_dir DoNotStarveTogether -cluster <cluster> -shard Caves
```

## Notes

- `cluster_token.txt` is included in backups because token material is required to restart the server, but support summaries should redact it.
- The launch command uses `-persistent_storage_root`, `-conf_dir`, `-cluster`, and `-shard`. Network/player settings are written to INI files instead of passed as command-line overrides.
- The initial install UI intentionally avoids large freeform Lua editors. Edit generated `worldgenoverride.lua` or `modoverrides.lua` after install for advanced world/mod configuration.
- `dedicated_server_mods_setup.lua` downloads Workshop mods; `modoverrides.lua` enables/configures them.
- Existing generated worlds may not pick up all `worldgenoverride.lua` changes.

## Trust Note

WindowsGSH does not create, own, review, sign, or guarantee third-party modules. If you download and run one, responsibility for that module is yours.
