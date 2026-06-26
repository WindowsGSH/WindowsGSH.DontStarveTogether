using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using WindowsGSH.Core.Modules;
using WindowsGSH.Core.Servers;

namespace WindowsGSH.Modules.DontStarveTogether;

public sealed class DontStarveTogetherModule : IGameServerModule, IManifestBackedModule, IModuleConsoleCommandCapability
{
    private static readonly Regex WorkshopIdPattern = new(@"^\d+$", RegexOptions.Compiled);

    private ModuleManifest? _manifest;
    private string _moduleDirectory = AppContext.BaseDirectory;

    private ModuleManifest Manifest => _manifest ??= ModuleManifest.Load(Path.Combine(_moduleDirectory, "module.json"));

    public string Id => Manifest.Id;
    public string Name => Manifest.Name;
    public string Version => Manifest.Version;
    public ModuleCapabilities Capabilities => Manifest.ToCapabilities(supportsQuery: true, supportsRcon: false) with { SupportsConsoleCommands = true };
    public SteamInstallDefinition? SteamInstall => Manifest.ToSteamInstall();
    public ModuleRuntimeDefinition Runtime => Manifest.ToRuntime();

    public void Configure(ModuleManifest manifest, string moduleDirectory)
    {
        _manifest = manifest;
        _moduleDirectory = moduleDirectory;
    }

    public IReadOnlyList<ConfigFieldDefinition> GetConfigFields() => Manifest.ToConfigFields();
    public IReadOnlyList<ServerAddonDefinition> GetAddonDefinitions() => Manifest.ToAddons();
    public IReadOnlyList<ServerBackupTargetDefinition> GetBackupTargets() => Manifest.ToBackupTargets();
    public string GetServerName(IReadOnlyDictionary<string, object?> settings) => GetSetting(settings, "server.name", Name);

    public ServerDisplayInfo GetDisplayInfo(ServerInstance instance)
    {
        return new ServerDisplayInfo(
            GetSetting(instance, "network.ip", "0.0.0.0"),
            GetSetting(instance, "network.port", "10999"),
            GetSetting(instance, "server.maxPlayers", "6"));
    }

    public Task<IReadOnlyDictionary<string, object?>> ReadConfigFileSettingsAsync(ServerInstance instance, CancellationToken cancellationToken)
    {
        var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        var clusterPath = Path.Combine(GetClusterDirectory(instance), "cluster.ini");
        if (File.Exists(clusterPath))
        {
            var cluster = ReadIni(clusterPath);
            CopyIniValue(cluster, "NETWORK", "cluster_name", values, "server.name");
            CopyIniValue(cluster, "NETWORK", "cluster_description", values, "server.description");
            CopyIniValue(cluster, "NETWORK", "cluster_password", values, "server.password");
            CopyIniValue(cluster, "NETWORK", "cluster_intention", values, "server.intention");
            CopyIniGameMode(cluster, values);
            CopyIniValue(cluster, "GAMEPLAY", "max_players", values, "server.maxPlayers");
            CopyIniBool(cluster, "GAMEPLAY", "pvp", values, "server.pvp");
            CopyIniBool(cluster, "GAMEPLAY", "pause_when_empty", values, "server.pauseWhenEmpty");
            CopyIniBool(cluster, "MISC", "console_enabled", values, "server.consoleEnabled");
            CopyIniBool(cluster, "SHARD", "shard_enabled", values, "shard.enableCaves");
            CopyIniValue(cluster, "SHARD", "master_port", values, "network.clusterMasterPort");
            CopyIniValue(cluster, "SHARD", "cluster_key", values, "cluster.key");
        }

        var masterPath = Path.Combine(GetClusterDirectory(instance), "Master", "server.ini");
        if (File.Exists(masterPath))
        {
            var master = ReadIni(masterPath);
            CopyIniValue(master, "NETWORK", "server_port", values, "network.port");
            CopyIniValue(master, "STEAM", "master_server_port", values, "network.masterServerPort");
            CopyIniValue(master, "STEAM", "authentication_port", values, "network.authenticationPort");
        }

        var cavesPath = Path.Combine(GetClusterDirectory(instance), "Caves", "server.ini");
        if (File.Exists(cavesPath))
        {
            var caves = ReadIni(cavesPath);
            CopyIniValue(caves, "NETWORK", "server_port", values, "network.cavesPort");
            CopyIniValue(caves, "STEAM", "master_server_port", values, "network.cavesMasterServerPort");
            CopyIniValue(caves, "STEAM", "authentication_port", values, "network.cavesAuthenticationPort");
            values["shard.enableCaves"] = true;
        }

        var tokenPath = GetClusterTokenPath(instance);
        if (File.Exists(tokenPath))
        {
            values["server.token"] = File.ReadAllText(tokenPath).Trim();
        }

        return Task.FromResult<IReadOnlyDictionary<string, object?>>(values);
    }

    public Task WriteConfigFileSettingsAsync(ServerInstance instance, CancellationToken cancellationToken)
    {
        WriteConfigFiles(instance);
        return Task.CompletedTask;
    }

    public Task<InstallPlan> CreateInstallPlanAsync(ServerInstance instance, CancellationToken cancellationToken)
    {
        if (SteamInstall == null)
        {
            throw new NotSupportedException("Don't Starve Together module does not define a SteamCMD install.");
        }

        return Task.FromResult(new InstallPlan(
            "steamcmd",
            $"+force_install_dir \"{instance.InstallPath}\" +login anonymous +app_update {SteamInstall.AppId} validate +quit",
            instance.InstallPath,
            ["Installs Don't Starve Together Dedicated Server through SteamCMD app 343050."]));
    }

    public Task<ProcessStartInfo> CreateStartInfoAsync(ServerInstance instance, CancellationToken cancellationToken)
    {
        WriteConfigFiles(instance);
        return Task.FromResult(CreateShardStartInfo(instance, "Master", GetServerExecutable(instance)));
    }

    public async Task<Process?> StartAsync(ServerInstance instance, CancellationToken cancellationToken)
    {
        if (!IsInstallValid(instance))
        {
            throw new FileNotFoundException("Don't Starve Together dedicated server executable was not found.", GetServerExecutable(instance));
        }

        var process = new Process
        {
            StartInfo = await CreateStartInfoAsync(instance, cancellationToken),
            EnableRaisingEvents = true
        };
        process.Start();
        return process;
    }

    public Task<IReadOnlyList<Process>> StartAddonProcessesAsync(ServerInstance instance, CancellationToken cancellationToken)
    {
        if (!GetBool(instance, "shard.enableCaves"))
        {
            return Task.FromResult<IReadOnlyList<Process>>([]);
        }

        WriteConfigFiles(instance);
        var process = new Process
        {
            StartInfo = CreateShardStartInfo(instance, "Caves", GetServerExecutable(instance)),
            EnableRaisingEvents = true
        };
        process.Start();
        return Task.FromResult<IReadOnlyList<Process>>([process]);
    }

    public Task StopAsync(ServerInstance instance, CancellationToken cancellationToken)
    {
        return ModuleStopStrategyRunner.StopAsync(this, Manifest, instance, cancellationToken);
    }

    public bool IsInstallValid(ServerInstance instance)
    {
        return File.Exists(GetServerExecutable(instance));
    }

    public string? GetConsoleLogPath(ServerInstance instance)
    {
        var cluster = GetClusterDirectory(instance);
        var shardLog = Path.Combine(cluster, "Master", "server_log.txt");
        return File.Exists(shardLog) ? shardLog : Path.Combine(GetConfDirectory(instance), "server_log.txt");
    }

    public Task<string> ExecuteConsoleCommandAsync(ServerInstance instance, string command, CancellationToken cancellationToken)
    {
        ServerConsoleService.SendCommand(instance.Id, command);
        return Task.FromResult("Console command sent.");
    }

    public Task<string> ExecuteRconCommandAsync(ServerInstance instance, string command, CancellationToken cancellationToken)
    {
        throw new NotSupportedException("Don't Starve Together does not expose Source-style RCON in this module. Use console commands instead.");
    }

    public Task<QueryResult> QueryAsync(ServerInstance instance, CancellationToken cancellationToken)
    {
        var status = ServerProcessLocator.IsRunning(this, instance.InstallPath)
            ? ModuleServerStatus.Online
            : ModuleServerStatus.Offline;
        return Task.FromResult(new QueryResult(status, Message: "Process status only."));
    }

    public ServerAddonStatus GetAddonStatus(ServerInstance instance, string addonId)
    {
        return new ServerAddonStatus(addonId, IsInstalled: false, IsEnabled: false, StatusText: "Workshop mods are controlled by module settings.");
    }

    public Task InstallAddonAsync(ServerInstance instance, string addonId, CancellationToken cancellationToken)
    {
        throw new NotSupportedException("Add Workshop IDs in the module settings to download and enable DST mods.");
    }

    public Task RemoveAddonAsync(ServerInstance instance, string addonId, CancellationToken cancellationToken)
    {
        throw new NotSupportedException("Remove Workshop IDs from the module settings to stop enabling DST mods.");
    }

    private ProcessStartInfo CreateShardStartInfo(ServerInstance instance, string shardName, string executable)
    {
        return new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = Path.GetDirectoryName(executable) ?? instance.InstallPath,
            Arguments = BuildStartArguments(instance, shardName),
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = Runtime.AllowsEmbeddedConsole,
            RedirectStandardError = Runtime.AllowsEmbeddedConsole,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
    }

    private static void WriteConfigFiles(ServerInstance instance)
    {
        var clusterDirectory = GetClusterDirectory(instance);
        var masterDirectory = Path.Combine(clusterDirectory, "Master");
        Directory.CreateDirectory(clusterDirectory);
        Directory.CreateDirectory(masterDirectory);

        File.WriteAllText(Path.Combine(clusterDirectory, "cluster.ini"), BuildClusterIni(instance));
        File.WriteAllText(GetClusterTokenPath(instance), GetSetting(instance, "server.token", string.Empty).Trim() + Environment.NewLine);
        File.WriteAllText(Path.Combine(masterDirectory, "server.ini"), BuildShardServerIni(instance, isMaster: true, GetSetting(instance, "network.port", "10999")));
        File.WriteAllText(Path.Combine(masterDirectory, "worldgenoverride.lua"), BuildWorldgenOverride(GetSetting(instance, "worldgen.masterPreset", GetSetting(instance, "worldgen.preset", "SURVIVAL_TOGETHER"))));
        File.WriteAllText(Path.Combine(clusterDirectory, "modoverrides.lua"), BuildModOverrides(instance));

        if (GetBool(instance, "shard.enableCaves"))
        {
            var cavesDirectory = Path.Combine(clusterDirectory, "Caves");
            Directory.CreateDirectory(cavesDirectory);
            File.WriteAllText(Path.Combine(cavesDirectory, "server.ini"), BuildShardServerIni(instance, isMaster: false, GetSetting(instance, "network.cavesPort", "10998")));
            File.WriteAllText(Path.Combine(cavesDirectory, "worldgenoverride.lua"), BuildWorldgenOverride(GetSetting(instance, "worldgen.cavesPreset", "DST_CAVE")));
        }

        var modsDirectory = Path.Combine(instance.InstallPath, "mods");
        Directory.CreateDirectory(modsDirectory);
        File.WriteAllText(Path.Combine(modsDirectory, "dedicated_server_mods_setup.lua"), BuildDedicatedServerModsSetup(instance));
    }

    private static string BuildClusterIni(ServerInstance instance)
    {
        var lines = new StringBuilder();
        lines.AppendLine("[GAMEPLAY]");
        lines.AppendLine($"game_mode = {NormalizeGameMode(GetSetting(instance, "server.gameMode", "Survival"))}");
        lines.AppendLine($"max_players = {GetInt(instance, "server.maxPlayers", 6)}");
        lines.AppendLine($"pvp = {ToIniBool(GetBool(instance, "server.pvp"))}");
        lines.AppendLine($"pause_when_empty = {ToIniBool(GetBool(instance, "server.pauseWhenEmpty", true))}");
        lines.AppendLine();
        lines.AppendLine("[NETWORK]");
        lines.AppendLine($"cluster_name = {GetSetting(instance, "server.name", "WindowsGSH DST Server")}");
        lines.AppendLine($"cluster_description = {GetSetting(instance, "server.description", string.Empty)}");
        lines.AppendLine($"cluster_password = {GetSetting(instance, "server.password", string.Empty)}");
        lines.AppendLine($"cluster_intention = {GetSetting(instance, "server.intention", "cooperative")}");
        lines.AppendLine("cluster_language = en");
        lines.AppendLine($"tick_rate = {GetInt(instance, "server.tickRate", 15)}");
        lines.AppendLine();
        lines.AppendLine("[MISC]");
        lines.AppendLine($"console_enabled = {ToIniBool(GetBool(instance, "server.consoleEnabled", true))}");
        lines.AppendLine($"max_snapshots = {GetInt(instance, "server.maxSnapshots", 6)}");
        lines.AppendLine();
        lines.AppendLine("[SHARD]");
        lines.AppendLine($"shard_enabled = {ToIniBool(GetBool(instance, "shard.enableCaves"))}");
        lines.AppendLine("bind_ip = 127.0.0.1");
        lines.AppendLine("master_ip = 127.0.0.1");
        lines.AppendLine($"master_port = {GetInt(instance, "network.clusterMasterPort", 11000)}");
        lines.AppendLine($"cluster_key = {GetSetting(instance, "cluster.key", "windowsgsh")}");
        return lines.ToString();
    }

    private static string BuildLegacySettingsIni(ServerInstance instance)
    {
        var lines = new StringBuilder();
        lines.AppendLine("[network]");
        lines.AppendLine($"default_server_name = {GetSetting(instance, "server.name", "WindowsGSH DST Server")}");
        lines.AppendLine($"server_description = {GetSetting(instance, "server.description", string.Empty)}");
        lines.AppendLine($"server_port = {GetInt(instance, "network.port", 10999)}");
        lines.AppendLine($"server_password = {GetSetting(instance, "server.password", string.Empty)}");
        lines.AppendLine($"max_players = {GetInt(instance, "server.maxPlayers", 6)}");
        lines.AppendLine();
        lines.AppendLine("[gameplay]");
        lines.AppendLine($"game_mode = {NormalizeGameMode(GetSetting(instance, "server.gameMode", "Survival"))}");
        lines.AppendLine($"pvp = {ToIniBool(GetBool(instance, "server.pvp"))}");
        lines.AppendLine($"pause_when_empty = {ToIniBool(GetBool(instance, "server.pauseWhenEmpty", true))}");
        lines.AppendLine();
        lines.AppendLine("[misc]");
        lines.AppendLine($"console_enabled = {ToIniBool(GetBool(instance, "server.consoleEnabled", true))}");
        lines.AppendLine($"max_snapshots = {GetInt(instance, "server.maxSnapshots", 6)}");
        lines.AppendLine();
        lines.AppendLine("[steam]");
        lines.AppendLine($"master_server_port = {GetInt(instance, "network.masterServerPort", 27018)}");
        lines.AppendLine($"authentication_port = {GetInt(instance, "network.authenticationPort", 8768)}");
        return lines.ToString();
    }

    private static string BuildShardServerIni(ServerInstance instance, bool isMaster, string port)
    {
        var shardName = isMaster ? "Master" : "Caves";
        var lines = new StringBuilder();
        lines.AppendLine("[NETWORK]");
        lines.AppendLine($"server_port = {port}");
        lines.AppendLine();
        lines.AppendLine("[SHARD]");
        lines.AppendLine($"is_master = {ToIniBool(isMaster)}");
        lines.AppendLine($"name = {shardName}");
        lines.AppendLine();
        lines.AppendLine("[STEAM]");
        lines.AppendLine($"master_server_port = {(isMaster ? GetInt(instance, "network.masterServerPort", 27018) : GetInt(instance, "network.cavesMasterServerPort", 27019))}");
        lines.AppendLine($"authentication_port = {(isMaster ? GetInt(instance, "network.authenticationPort", 8768) : GetInt(instance, "network.cavesAuthenticationPort", 8769))}");
        return lines.ToString();
    }

    private static string BuildWorldgenOverride(string preset)
    {
        return $$"""
return {
  override_enabled = true,
  preset = "{{EscapeLua(preset)}}",
  overrides = {}
}
""";
    }

    private static string BuildDedicatedServerModsSetup(ServerInstance instance)
    {
        var lines = new StringBuilder();
        lines.AppendLine("-- Generated by WindowsGSH. Edit Workshop IDs in the module settings.");
        foreach (var id in GetWorkshopIds(instance))
        {
            lines.AppendLine($"ServerModSetup(\"{id}\")");
        }

        return lines.ToString();
    }

    private static string BuildModOverrides(ServerInstance instance)
    {
        var lines = new StringBuilder();
        lines.AppendLine("return {");
        foreach (var id in GetWorkshopIds(instance))
        {
            lines.AppendLine($"  [\"workshop-{id}\"] = {{ enabled = true, configuration_options = {{}} }},");
        }

        lines.AppendLine("}");
        return lines.ToString();
    }

    private static string BuildStartArguments(ServerInstance instance, string shardName)
    {
        var parts = new List<string>
        {
            "-console",
            "-persistent_storage_root",
            WindowsCommandLineEscaper.Quote(GetProfileStorageRoot(instance)),
            "-conf_dir",
            WindowsCommandLineEscaper.Quote(GetSetting(instance, "profile.confDir", "DoNotStarveTogether")),
            "-cluster",
            WindowsCommandLineEscaper.Quote(GetSetting(instance, "cluster.name", "MyDediServer")),
            "-shard",
            WindowsCommandLineEscaper.Quote(shardName)
        };

        var additional = BuildAdditionalArguments(instance);
        if (!string.IsNullOrWhiteSpace(additional))
        {
            parts.Add(additional);
        }

        return string.Join(" ", parts);
    }

    private static string BuildAdditionalArguments(ServerInstance instance)
    {
        var parts = new List<string>();
        if (GetBool(instance, "server.offline"))
        {
            parts.Add("-offline");
        }

        if (GetBool(instance, "server.disableDataCollection"))
        {
            parts.Add("-disabledatacollection");
        }

        if (GetBool(instance, "mods.skipUpdate"))
        {
            parts.Add("-skip_update_server_mods");
        }

        var custom = GetSetting(instance, "server.additionalArguments", string.Empty);
        if (!string.IsNullOrWhiteSpace(custom))
        {
            parts.Add(custom);
        }

        return string.Join(" ", parts);
    }

    private static IReadOnlyList<string> GetWorkshopIds(ServerInstance instance)
    {
        var value = GetSetting(instance, "mods.workshopIds", string.Empty);
        return value
            .Split([',', ';', ' ', '\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(id => WorkshopIdPattern.IsMatch(id))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static string GetServerExecutable(ServerInstance instance)
    {
        var x64 = Path.Combine(instance.InstallPath, "bin64", "dontstarve_dedicated_server_nullrenderer_x64.exe");
        if (File.Exists(x64))
        {
            return x64;
        }

        return Path.Combine(instance.InstallPath, "bin", "dontstarve_dedicated_server_nullrenderer.exe");
    }

    private static string GetClusterTokenPath(ServerInstance instance)
    {
        return Path.Combine(GetClusterDirectory(instance), "cluster_token.txt");
    }

    private static string GetClusterDirectory(ServerInstance instance)
    {
        return Path.Combine(GetConfDirectory(instance), GetSetting(instance, "cluster.name", "MyDediServer"));
    }

    private static string GetConfDirectory(ServerInstance instance)
    {
        return Path.Combine(GetProfileStorageRoot(instance), GetSetting(instance, "profile.confDir", "DoNotStarveTogether"));
    }

    private static string GetProfileStorageRoot(ServerInstance instance)
    {
        var configured = GetSetting(instance, "profile.root", "KleiConfig");
        if (Path.IsPathRooted(configured))
        {
            return Path.GetFullPath(configured);
        }

        var root = Path.GetFullPath(Path.Combine(instance.InstallPath, configured));
        var install = Path.TrimEndingDirectorySeparator(Path.GetFullPath(instance.InstallPath));
        if (!root.StartsWith(install + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(root, install, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Klei Profile Root must stay inside the server files folder unless it is an absolute path.");
        }

        return root;
    }

    private static Dictionary<string, Dictionary<string, string>> ReadIni(string path)
    {
        var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        var section = string.Empty;
        foreach (var rawLine in File.ReadAllLines(path))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal) || line.StartsWith(";", StringComparison.Ordinal))
            {
                continue;
            }

            if (line.StartsWith("[", StringComparison.Ordinal) && line.EndsWith("]", StringComparison.Ordinal))
            {
                section = line[1..^1].Trim();
                if (!result.ContainsKey(section))
                {
                    result[section] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                }

                continue;
            }

            var separator = line.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            if (!result.TryGetValue(section, out var values))
            {
                values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                result[section] = values;
            }

            values[line[..separator].Trim()] = line[(separator + 1)..].Trim();
        }

        return result;
    }

    private static void CopyIniValue(
        IReadOnlyDictionary<string, Dictionary<string, string>> ini,
        string section,
        string key,
        IDictionary<string, object?> values,
        string targetKey)
    {
        if (ini.TryGetValue(section, out var sectionValues) &&
            sectionValues.TryGetValue(key, out var value) &&
            !string.IsNullOrWhiteSpace(value))
        {
            values[targetKey] = value;
        }
    }

    private static void CopyIniBool(
        IReadOnlyDictionary<string, Dictionary<string, string>> ini,
        string section,
        string key,
        IDictionary<string, object?> values,
        string targetKey)
    {
        if (ini.TryGetValue(section, out var sectionValues) &&
            sectionValues.TryGetValue(key, out var value))
        {
            values[targetKey] = IsTruthy(value.Trim().ToLowerInvariant());
        }
    }

    private static void CopyIniGameMode(
        IReadOnlyDictionary<string, Dictionary<string, string>> ini,
        IDictionary<string, object?> values)
    {
        if (ini.TryGetValue("GAMEPLAY", out var sectionValues) &&
            sectionValues.TryGetValue("game_mode", out var value) &&
            !string.IsNullOrWhiteSpace(value))
        {
            values["server.gameMode"] = value.Trim().ToLowerInvariant() switch
            {
                "relaxed" => "Relaxed",
                "endless" => "Endless",
                "wilderness" => "Wilderness",
                "lightsout" => "Lights Out",
                _ => "Survival"
            };
        }
    }

    private static string NormalizeGameMode(string value)
    {
        return value.Trim().ToLowerInvariant().Replace(" ", string.Empty, StringComparison.Ordinal) switch
        {
            "relaxed" => "relaxed",
            "endless" => "endless",
            "wilderness" => "wilderness",
            "lightsout" => "lightsout",
            _ => "survival"
        };
    }

    private static string EnsureTrailingNewLine(string value)
    {
        return value.EndsWith(Environment.NewLine, StringComparison.Ordinal) ? value : value + Environment.NewLine;
    }

    private static string EscapeLua(string value)
    {
        return value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
    }

    private static string ToIniBool(bool value) => value ? "true" : "false";

    private static bool IsTruthy(string value)
    {
        return bool.TryParse(value, out var parsed)
            ? parsed
            : value is "1" or "yes" or "on";
    }

    private static bool GetBool(ServerInstance instance, string key, bool fallback = false)
    {
        if (!instance.Settings.TryGetValue(key, out var value) || value == null)
        {
            return fallback;
        }

        return value switch
        {
            bool boolean => boolean,
            string text => IsTruthy(text.Trim().ToLowerInvariant()),
            _ => fallback
        };
    }

    private static int GetInt(ServerInstance instance, string key, int fallback)
    {
        var value = GetSetting(instance, key, fallback.ToString(CultureInfo.InvariantCulture));
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : fallback;
    }

    private static string GetSetting(ServerInstance instance, string key, string fallback)
    {
        return GetSetting(instance.Settings, key, fallback);
    }

    private static string GetSetting(IReadOnlyDictionary<string, object?> settings, string key, string fallback)
    {
        return settings.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value?.ToString())
            ? value.ToString()!.Trim()
            : fallback;
    }
}
