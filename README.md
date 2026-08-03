# Don't Starve Together Dedicated Server

[![WindowsGSH](.github/assets/windowsgsh-badge.svg)](https://windowsgsh.com)
[![Status](https://img.shields.io/badge/status-beta_candidate-1E8449)](#status)
[![Module version](https://img.shields.io/badge/dynamic/json?url=https%3A%2F%2Fraw.githubusercontent.com%2FWindowsGSH%2FWindowsGSH.DontStarveTogether%2Fmain%2FDontStarveTogether.mod%2Fmodule.json&query=%24.version&prefix=v&label=module&color=1E8449)](DontStarveTogether.mod/module.json)
[![Requires WindowsGSH](https://img.shields.io/badge/dynamic/json?url=https%3A%2F%2Fraw.githubusercontent.com%2FWindowsGSH%2FWindowsGSH.DontStarveTogether%2Fmain%2FDontStarveTogether.mod%2Fmodule.json%3Fbadge%3Dminimum&query=%24.minimumWindowsGshVersion&prefix=v&label=requires%20WindowsGSH&color=2563EB)](DontStarveTogether.mod/module.json)
[![Licence](https://img.shields.io/badge/licence-MIT-64748B)](LICENSE.md)

This WindowsGSH module installs, configures, starts, monitors, and backs up a Don't Starve Together dedicated server with optional Master and Caves shards.

## Status

**BETA CANDIDATE.** The module has a native WindowsGSH implementation, models both shards, and checks the executable, cluster profile, and required cluster token through Readiness Check. Its current dedicated-server build still needs the live checks below before it is marked beta tested.

## Installation

WindowsGSH installs SteamCMD app `343050` using anonymous login. SteamCMD supplies the server program, but it does not create the Klei cluster token required for an online server.

### Get a Klei cluster token

1. Sign in to the [Klei Account game-server page](https://accounts.klei.com/account/game/servers?game=DontStarveTogether) with the account that owns Don't Starve Together.
2. Generate a new Don't Starve Together server token and copy only the token value.
3. Import `DontStarveTogether.mod` into WindowsGSH and create the server.
4. Paste the value into **Klei Cluster Token** (`server.token`) before starting the server. WindowsGSH writes it to the selected cluster's `cluster_token.txt` file.

Alternatively, a logged-in game client can generate `cluster_token.txt` with the in-game console command `TheNet:GenerateClusterToken()`. Copy the file's token value into the WindowsGSH setting. Klei's server output also directs owners to these two methods when no token is available.

The token is tied to the Klei account and must be treated like a password. Do not add quotes, labels, or surrounding whitespace, do not publish it in screenshots or support logs, and revoke/replace it through Klei if it is exposed. An absent, invalid, or malformed value normally produces `E_INVALID_TOKEN` / `No auth token could be found` and prevents an online server from starting.

The preferred executable is `bin64/dontstarve_dedicated_server_nullrenderer_x64.exe`, with the 32-bit null-renderer executable as a fallback. Server profiles are isolated below the selected server's files directory rather than written to the user's normal Documents profile.

### Import an existing server

WindowsGSH can import either a normal dedicated-server installation folder or a WindowsGSM server folder containing `serverfiles`. The executable is adopted or copied, but Klei profile/cluster data stored outside that installation is not guessed or copied automatically. Review the preview, select the correct profile/cluster settings, and copy the cluster files and token separately when migrating an external layout.

## Configuration

- Cluster identity, password, game mode, player limit, gameplay, shard, cluster-port, and cluster-key values are written to `cluster.ini`.
- The Klei token is written to `cluster_token.txt`; Klei requires this plaintext file even though the value is secret.
- `profile.root`, `profile.confDir`, and `cluster.name` choose the isolated Klei profile and cluster directories used by both shard commands.
- Master and Caves game, Steam master, and authentication ports are written to each shard's `server.ini`.
- `shard.enableCaves` controls creation and startup of the Caves shard.
- Workshop IDs generate `mods/dedicated_server_mods_setup.lua`; supplied or generated overrides go to `modoverrides.lua`.
- Master and Caves presets generate their respective `worldgenoverride.lua` files.
- `server.disableDataCollection` adds `-disabledatacollection`; leave it disabled for a public server unless live testing confirms the server remains discoverable.

The launch command uses `-persistent_storage_root`, `-conf_dir`, `-cluster`, and `-shard`. Advanced Lua may be edited after installation, but a later WindowsGSH save can rewrite files owned by these settings.

## Networking

| Purpose | Default | Protocol | Exposure |
| --- | ---: | --- | --- |
| Master game | `10999` | UDP | Public; eligible for firewall guidance and UPnP |
| Master Steam | `27018` | UDP | Public; eligible for firewall guidance and UPnP |
| Master authentication | `8768` | UDP | Public; eligible for firewall guidance and UPnP |
| Caves game | `10998` | UDP | Public when Caves is enabled; eligible for firewall guidance and UPnP |
| Caves Steam | `27019` | UDP | Public when Caves is enabled; eligible for firewall guidance and UPnP |
| Caves authentication | `8769` | UDP | Public when Caves is enabled; eligible for firewall guidance and UPnP |
| Local shard communication | `11000` | UDP | Private; never eligible for automatic external forwarding |

All values are configurable. WindowsGSH's current port contract cannot conditionally remove declarations, so an automatic UPnP policy can request the three Caves mappings even when the Caves shard is disabled. Those ports have no game listener in that state; use Manual forwarding if the extra router mappings are unwanted.

## Query, console, and administration

Status is process-based; the module does not claim a player-query protocol or report live player counts. It sends console commands through redirected standard input. Graceful shutdown uses `c_shutdown(true)`, but both-shard delivery remains part of the live verification checklist. The module does not implement RCON or another remote administration client.

## Files and backups

The managed profile is below `KleiConfig/DoNotStarveTogether/<cluster>/` and includes `cluster.ini`, `cluster_token.txt`, `modoverrides.lua`, and the Master/Caves `server.ini` and `worldgenoverride.lua` files. Workshop setup is stored under `mods/dedicated_server_mods_setup.lua`.

Backups include the cluster profile so worlds and the token required to restart them remain together. Treat backup archives as sensitive, never attach `cluster_token.txt` to a public support report, and test restoration with both shards stopped.

## Known limitations

- Master and Caves process tracking, command delivery, reattachment, and graceful shutdown require live verification together.
- The host cannot yet make the Caves port declarations conditional on `shard.enableCaves`.
- Process supervision does not provide player counts or prove that the server is externally reachable.
- Existing worlds may not apply later `worldgenoverride.lua` changes.
- The settings UI deliberately avoids large free-form Lua editors; advanced mod and world configuration remains a file-editing task.

## Beta verification checklist

- [ ] Generate a Klei token, fresh-install Steam app `343050`, and confirm the preferred x64 executable is selected.
- [ ] Save settings, confirm all cluster/shard files contain the intended values, and verify secrets do not appear in logs or diagnostics.
- [ ] Start Master and Caves, verify both processes are tracked and reattached after restarting WindowsGSH, then stop both cleanly.
- [ ] Verify local and remote joining, every declared UDP port, UPnP behavior with Caves enabled/disabled, and the private cluster port.
- [ ] Test Workshop updates, application update/verify behavior, crash handling, backup, and a full two-shard restore.

## Support

Report module problems through the [WindowsGSH.DontStarveTogether issue tracker](https://github.com/WindowsGSH/WindowsGSH.DontStarveTogether/issues). Include the module and WindowsGSH versions, sanitized server configuration, the operation performed, relevant logs, and whether Master, Caves, or both shards were enabled. Do not post the cluster token, passwords, or unredacted backup archives.

## Support development

If you like the work I do and would like to support continued WindowsGSH and module development, you can contribute here:

- [Ko-fi](https://ko-fi.com/shenniko)
- [PayPal](https://paypal.me/shenniko)

## Trust and source

Modules execute with the same Windows permissions as WindowsGSH. Download releases from a source you trust and review [`DontStarveTogetherModule.cs`](DontStarveTogether.mod/DontStarveTogetherModule.cs) and [`module.json`](DontStarveTogether.mod/module.json) before running them. See [SECURITY.md](SECURITY.md) for safe-download, credential-handling, and private vulnerability-reporting guidance.
