#pragma warning disable MEAI001

using System.ClientModel;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;
using OpenAI;
using Spectre.Console;

namespace DbAgent;

internal static class Program
{
    private static bool _assistantHeaderPrinted;
    private static bool _hasStreamedText;
    private static string? _transientStatusText;

    private static readonly HashSet<string> ReadOnlyTools = new(StringComparer.OrdinalIgnoreCase)
    {
        "testconnection",
        "testconnectionbyname",
        "getdatabaseconfig",
        "validateconfiguration",
        "listdatabases",
        "switchdatabase",
        "getcurrentdatabase",
        "healthcheck",
        "testconnectionwithretry",

        "sqlquery",
        "sqlquerysingle",
        "getdatasetall",
        "getscalar",
        "sqlquerywithinparameter",

        "getdatabaselist",
        "getviewinfolist",
        "gettableinfolist",
        "getcolumninfosbytablename",
        "gettableschema",
        "getisidentities",
        "getprimaries",
        "getindexlist",
        "getproclist",
        "getfunclist",
        "gettriggernames",
        "isanytable",
        "isanycolumn",
        "isanyconstraint",
        "isanytableremark",

        "generatedatabasedocumentation"
    };

    private static readonly HashSet<string> WriteTools = new(StringComparer.OrdinalIgnoreCase)
    {
        "executecommand",
        "callstoredprocedure",
        "callstoredprocedurewithoutput",
        "executecommandwithgo",
        "batchexecutecommands"
    };

    private static readonly HashSet<string> SchemaWriteTools = new(StringComparer.OrdinalIgnoreCase)
    {
        "droptable",
        "truncatetable",
        "backuptable",
        "renametable",

        "addcolumn",
        "updatecolumn",
        "dropcolumn",
        "renamecolumn",

        "addprimarykey",
        "dropconstraint",
        "createindex",

        "adddefaultvalue",
        "addtableremark",
        "deletetableremark",
        "addcolumnremark",
        "deletecolumnremark",

        "dropview",
        "dropfunc",
        "dropproc"
    };

    private static readonly HashSet<string> ApprovalRequiredTools = new(StringComparer.OrdinalIgnoreCase)
    {
        "executecommand",
        "callstoredprocedure",
        "callstoredprocedurewithoutput",
        "executecommandwithgo",
        "batchexecutecommands",

        "droptable",
        "truncatetable",
        "backuptable",
        "renametable",

        "addcolumn",
        "updatecolumn",
        "dropcolumn",
        "renamecolumn",

        "addprimarykey",
        "dropconstraint",
        "createindex",

        "adddefaultvalue",
        "addtableremark",
        "deletetableremark",
        "addcolumnremark",
        "deletecolumnremark",

        "dropview",
        "dropfunc",
        "dropproc"
    };

    private static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        CliOptions options;

        try
        {
            options = CliOptions.Parse(args);
        }
        catch (Exception ex)
        {
            PrintErrorPanel(ex.Message);
            PrintCliHelp();
            return 2;
        }

        if (options.ShowHelp)
        {
            PrintCliHelp();
            return 0;
        }

        if (options.Command.Equals("init", StringComparison.OrdinalIgnoreCase))
        {
            return RunInit(options);
        }

        string appConfigPath;

        try
        {
            appConfigPath = ResolveConfigPath(options.ConfigPath);
        }
        catch (Exception ex)
        {
            PrintErrorPanel(ex.Message);
            AnsiConsole.MarkupLine("[grey]提示：首次使用可运行 [white]dbagent init[/] 生成配置。[/]");
            return 2;
        }

        AppConfig appConfig;

        try
        {
            appConfig = LoadConfig(appConfigPath);

            if (NormalizeConfig(appConfig))
            {
                SaveConfig(appConfig, appConfigPath);
            }
        }
        catch (Exception ex)
        {
            PrintErrorPanel($"读取配置失败：{ex.Message}");
            return 2;
        }

        if (options.Command.Equals("doctor", StringComparison.OrdinalIgnoreCase))
        {
            return await RunDoctorAsync(
                appConfig,
                appConfigPath,
                options.ModelName).ConfigureAwait(false);
        }

        if (options.Command.Equals("models", StringComparison.OrdinalIgnoreCase))
        {
            PrintModelProfiles(appConfig);
            return 0;
        }

        if (!options.Command.Equals("chat", StringComparison.OrdinalIgnoreCase))
        {
            PrintErrorPanel($"未知命令：{options.Command}");
            PrintCliHelp();
            return 2;
        }

        var configDirectory = Path.GetDirectoryName(appConfigPath)!;
        var databaseConfigPath = ResolvePath(configDirectory, appConfig.Database.ConfigPath);

        try
        {
            ValidateConfig(appConfig, databaseConfigPath);
        }
        catch (Exception ex)
        {
            PrintErrorPanel($"配置无效：{ex.Message}");
            return 2;
        }

        var selectedProfile = GetStartupProfile(appConfig, options.ModelName);

        var mcpEnvironment = BuildMcpEnvironment(appConfig, databaseConfigPath);

        var transport = new StdioClientTransport(new()
        {
            Name = "DatabaseMcpServer",
            Command = appConfig.Mcp.Command,
            Arguments = appConfig.Mcp.Arguments,
            EnvironmentVariables = mcpEnvironment
        });

        await using var mcpClient = await McpClient.CreateAsync(transport).ConfigureAwait(false);

        var allMcpTools = await mcpClient.ListToolsAsync().ConfigureAwait(false);

        var exposedMcpTools = allMcpTools
            .Where(tool => IsAllowedTool(tool.Name, appConfig.Safety))
            .ToArray();

        AITool[] exposedTools = exposedMcpTools
            .Cast<AITool>()
            .Select(WrapToolForApprovalIfNeeded)
            .ToArray();

        var agent = CreateAgent(
            selectedProfile,
            appConfig.Safety,
            exposedTools);

        var session = await agent.CreateSessionAsync().ConfigureAwait(false);

        var state = new RuntimeState
        {
            Config = appConfig,
            CurrentProfile = selectedProfile,
            Agent = agent,
            Session = session,
            SessionUsage = new TokenUsageAccumulator(),
            ConfigPath = appConfigPath
        };

        AnsiConsole.Clear();

        PrintCompactHeader(
            state.CurrentProfile,
            state.Config.Safety,
            exposedTools.Length,
            allMcpTools.Count);

        if (exposedTools.Length == 0)
        {
            PrintWarningPanel("没有暴露任何 MCP 工具。请检查 DatabaseMcpServer 是否正确启动，以及工具名称是否匹配。");
        }

        AnsiConsole.MarkupLine("[grey]输入 [white]/help[/] 查看命令。[/]");

        while (true)
        {
            AnsiConsole.WriteLine();
            PrintDivider();

            AnsiConsole.Markup("[bold blue]你[/][grey]> [/]");
            var input = Console.ReadLine();

            if (string.IsNullOrWhiteSpace(input))
            {
                continue;
            }

            var trimmed = input.Trim();

            if (trimmed.Equals("/exit", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Equals("/quit", StringComparison.OrdinalIgnoreCase))
            {
                return 0;
            }

            if (trimmed.Equals("/help", StringComparison.OrdinalIgnoreCase))
            {
                PrintHelp();
                continue;
            }

            if (trimmed.Equals("/tools", StringComparison.OrdinalIgnoreCase))
            {
                PrintTools(
                    allMcpTools.Select(t => t.Name),
                    exposedMcpTools.Select(t => t.Name));
                continue;
            }

            if (trimmed.Equals("/config", StringComparison.OrdinalIgnoreCase))
            {
                PrintConfig(
                    state.Config,
                    state.CurrentProfile,
                    state.ConfigPath,
                    databaseConfigPath);
                continue;
            }

            if (trimmed.Equals("/stats", StringComparison.OrdinalIgnoreCase))
            {
                PrintDetailedTokenUsage(state.SessionUsage);
                continue;
            }

            if (trimmed.Equals("/clear", StringComparison.OrdinalIgnoreCase))
            {
                state.Session = await state.Agent.CreateSessionAsync().ConfigureAwait(false);
                state.SessionUsage.Reset();
                PrintSuccessPanel("会话已清空。");
                continue;
            }

            if (trimmed.Equals("/models", StringComparison.OrdinalIgnoreCase))
            {
                PrintModelProfiles(state.Config);
                continue;
            }

            if (trimmed.Equals("/model", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("/model ", StringComparison.OrdinalIgnoreCase))
            {
                await HandleModelCommandAsync(
                    trimmed,
                    state,
                    exposedTools).ConfigureAwait(false);

                continue;
            }

            var turnUsage = new TokenUsageAccumulator();

            try
            {
                ResetTurnUiState();
                ShowTransientStatus("DbAgent · 思考中…", "yellow");

                await RunStreamingWithApprovalsAsync(
                    state.Agent,
                    trimmed,
                    state.Session,
                    turnUsage);

                ClearTransientStatus();

                if (_hasStreamedText)
                {
                    Console.WriteLine();
                }

                state.SessionUsage.Add(turnUsage);
                PrintCompactTokenUsage(turnUsage, state.SessionUsage);
            }
            catch (Exception ex)
            {
                ClearTransientStatus();
                PrintErrorPanel($"执行失败: {ex.Message}");
            }
        }
    }

    private static int RunInit(CliOptions options)
    {
        var configPath = !string.IsNullOrWhiteSpace(options.ConfigPath)
            ? Path.GetFullPath(options.ConfigPath)
            : GetDefaultConfigPath();

        var configDirectory = Path.GetDirectoryName(configPath)!;
        var databasePath = Path.Combine(configDirectory, "databases.json");

        Directory.CreateDirectory(configDirectory);

        if (!options.Force && (File.Exists(configPath) || File.Exists(databasePath)))
        {
            PrintWarningPanel(
                $"配置文件已存在。若要覆盖，请使用 --force。\n{configPath}");
            return 1;
        }

        File.WriteAllText(configPath, DefaultAppSettingsJson);
        File.WriteAllText(databasePath, DefaultDatabasesJson);

        PrintSuccessPanel(
            $"已生成配置：\n{configPath}\n{databasePath}");

        AnsiConsole.MarkupLine("[grey]下一步：编辑配置后运行 [white]dbagent[/]。[/]");
        return 0;
    }

    private static string ResolveConfigPath(string? explicitConfigPath)
    {
        var candidates = new List<string>();

        if (!string.IsNullOrWhiteSpace(explicitConfigPath))
        {
            candidates.Add(Path.GetFullPath(explicitConfigPath));
        }

        var envConfig = Environment.GetEnvironmentVariable("DBAGENT_CONFIG");
        if (!string.IsNullOrWhiteSpace(envConfig))
        {
            candidates.Add(Path.GetFullPath(envConfig));
        }

        candidates.Add(Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json"));
        candidates.Add(GetDefaultConfigPath());
        candidates.Add(Path.Combine(AppContext.BaseDirectory, "appsettings.json"));

        var found = candidates.FirstOrDefault(File.Exists);

        return found ?? throw new FileNotFoundException(
            "找不到 appsettings.json。可运行 dbagent init 生成默认配置，或使用 --config 指定配置文件。");
    }

    private static string GetDefaultConfigPath()
    {
        var userHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(userHome, ".dbagent", "appsettings.json");
    }

    private static Dictionary<string, string?> BuildMcpEnvironment(
        AppConfig config,
        string databaseConfigPath)
    {
        var environment = new Dictionary<string, string?>
        {
            ["DB_CONFIG_PATH"] = databaseConfigPath
        };

        AddIfPresent(environment, "SEQ_SERVER_URL", config.Database.SeqServerUrl);
        AddIfPresent(environment, "SEQ_API_KEY", config.Database.SeqApiKey);
        AddIfPresent(environment, "DB_DDL_WHITELIST", config.Database.DdlWhitelist);

        return environment;
    }

    private static async Task<int> RunDoctorAsync(
        AppConfig config,
        string appConfigPath,
        string? modelName)
    {
        var checks = new List<DoctorCheck>();
        var configDirectory = Path.GetDirectoryName(appConfigPath)!;
        var databaseConfigPath = ResolvePath(
            configDirectory,
            config.Database.ConfigPath);

        checks.Add(new DoctorCheck(
            "应用配置",
            true,
            appConfigPath));

        ModelProfile? profile = null;

        try
        {
            profile = GetStartupProfile(config, modelName);
            ValidateModelProfile(profile);
            _ = ResolveApiKey(profile);

            checks.Add(new DoctorCheck(
                "模型配置",
                true,
                $"{profile.Name} · {profile.Model}"));
        }
        catch (Exception ex)
        {
            checks.Add(new DoctorCheck(
                "模型配置",
                false,
                ex.Message));
        }

        if (profile is not null)
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                var stopwatch = Stopwatch.StartNew();
                var chatClient = CreateChatClient(profile);

                await chatClient.GetResponseAsync(
                    "Reply only with OK.",
                    cancellationToken: cts.Token).ConfigureAwait(false);

                stopwatch.Stop();

                checks.Add(new DoctorCheck(
                    "模型连通性",
                    true,
                    $"{stopwatch.ElapsedMilliseconds} ms"));
            }
            catch (Exception ex)
            {
                checks.Add(new DoctorCheck(
                    "模型连通性",
                    false,
                    ex.Message));
            }
        }

        var databaseConfigInspection = InspectDatabaseConfigFile(databaseConfigPath);

        checks.Add(new DoctorCheck(
            "数据库配置文件",
            databaseConfigInspection.IsOk,
            databaseConfigInspection.Detail));

        if (!databaseConfigInspection.IsOk)
        {
            PrintDoctorReport(checks);
            return 1;
        }

        try
        {
            var transport = new StdioClientTransport(new()
            {
                Name = "DatabaseMcpServer",
                Command = config.Mcp.Command,
                Arguments = config.Mcp.Arguments,
                EnvironmentVariables = BuildMcpEnvironment(
                    config,
                    databaseConfigPath)
            });

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

            await using var mcpClient = await McpClient
                .CreateAsync(transport, cancellationToken: cts.Token)
                .ConfigureAwait(false);

            var tools = await mcpClient
                .ListToolsAsync(cancellationToken: cts.Token)
                .ConfigureAwait(false);

            checks.Add(new DoctorCheck(
                "MCP 启动",
                true,
                $"已连接，发现 {tools.Count} 个工具"));

            var validateConfigurationTool = FindToolName(
                tools.Select(t => t.Name),
                "validateconfiguration");

            if (validateConfigurationTool is not null)
            {
                var result = await mcpClient
                    .CallToolAsync(
                        validateConfigurationTool,
                        cancellationToken: cts.Token)
                    .ConfigureAwait(false);

                checks.Add(new DoctorCheck(
                    "MCP 配置验证",
                    result.IsError is not true,
                    result.IsError is true
                        ? "validate_configuration 返回错误"
                        : "通过"));
            }
            else
            {
                checks.Add(new DoctorCheck(
                    "MCP 配置验证",
                    false,
                    "未发现 validate_configuration 工具"));
            }

            var healthCheckTool = FindToolName(
                tools.Select(t => t.Name),
                "healthcheck");

            if (healthCheckTool is not null)
            {
                var result = await mcpClient
                    .CallToolAsync(
                        healthCheckTool,
                        cancellationToken: cts.Token)
                    .ConfigureAwait(false);

                checks.Add(new DoctorCheck(
                    "数据库健康检查",
                    result.IsError is not true,
                    result.IsError is true
                        ? "health_check 返回错误"
                        : "通过"));
            }
            else
            {
                checks.Add(new DoctorCheck(
                    "数据库健康检查",
                    false,
                    "未发现 health_check 工具"));
            }
        }
        catch (Exception ex)
        {
            checks.Add(new DoctorCheck(
                "MCP 启动",
                false,
                ex.Message));
        }

        PrintDoctorReport(checks);

        return checks.All(c => c.IsOk) ? 0 : 1;
    }

    private static (bool IsOk, string Detail) InspectDatabaseConfigFile(string path)
    {
        if (!File.Exists(path))
        {
            return (false, $"找不到文件：{path}");
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));

            if (!document.RootElement.TryGetProperty("databases", out var databases) ||
                databases.ValueKind != JsonValueKind.Array)
            {
                return (false, "缺少 databases 数组");
            }

            var databaseItems = databases.EnumerateArray().ToArray();

            if (databaseItems.Length == 0)
            {
                return (false, "databases 数组为空");
            }

            foreach (var database in databaseItems)
            {
                if (!database.TryGetProperty("name", out var name) ||
                    string.IsNullOrWhiteSpace(name.GetString()))
                {
                    return (false, "存在未配置 name 的数据库项");
                }

                if (!database.TryGetProperty("connectionString", out var connectionString) ||
                    string.IsNullOrWhiteSpace(connectionString.GetString()))
                {
                    return (false, "存在未配置 connectionString 的数据库项");
                }

                if (!database.TryGetProperty("dbType", out var dbType) ||
                    string.IsNullOrWhiteSpace(dbType.GetString()))
                {
                    return (false, "存在未配置 dbType 的数据库项");
                }
            }

            return (true, $"{databaseItems.Length} 个数据库配置");
        }
        catch (Exception ex)
        {
            return (false, $"JSON 无效：{ex.Message}");
        }
    }

    private static string? FindToolName(
        IEnumerable<string> toolNames,
        string normalizedExpectedName)
    {
        return toolNames.FirstOrDefault(toolName =>
            NormalizeToolName(toolName)
                .Equals(normalizedExpectedName, StringComparison.OrdinalIgnoreCase));
    }

    private static void PrintDoctorReport(IEnumerable<DoctorCheck> checks)
    {
        var items = checks.ToList();

        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Cyan1);

        table.AddColumn("[bold]检查项[/]");
        table.AddColumn("[bold]状态[/]");
        table.AddColumn("[bold]详情[/]");

        foreach (var check in items)
        {
            table.AddRow(
                Markup.Escape(check.Name),
                check.IsOk ? "[green]OK[/]" : "[red]FAIL[/]",
                Markup.Escape(check.Detail));
        }

        var panel = new Panel(table)
            .Header("[bold cyan]DbAgent Doctor[/]")
            .Border(BoxBorder.Rounded)
            .BorderColor(Color.Cyan1)
            .Expand();

        AnsiConsole.Write(panel);

        if (items.All(c => c.IsOk))
        {
            PrintSuccessPanel("所有检查均通过。");
        }
        else
        {
            PrintWarningPanel("存在未通过的检查。");
        }
    }

    private static IChatClient CreateChatClient(ModelProfile profile)
    {
        ValidateModelProfile(profile);

        var apiKey = ResolveApiKey(profile);

        var openAiClient = new OpenAIClient(
            new ApiKeyCredential(apiKey),
            new OpenAIClientOptions
            {
                Endpoint = new Uri(profile.Endpoint)
            });

        return openAiClient
            .GetChatClient(profile.Model)
            .AsIChatClient();
    }

    private static AIAgent CreateAgent(
        ModelProfile profile,
        SafetyConfig safety,
        AITool[] tools)
    {
        var chatClient = CreateChatClient(profile);

        return chatClient.AsAIAgent(
            name: "DbAgent",
            instructions: BuildInstructions(safety),
            tools: tools);
    }

    private static async Task HandleModelCommandAsync(
        string command,
        RuntimeState state,
        AITool[] exposedTools)
    {
        var remainder = command.Length == "/model".Length
            ? ""
            : command["/model".Length..].Trim();

        if (string.IsNullOrWhiteSpace(remainder))
        {
            await SelectModelInteractiveAsync(
                state,
                exposedTools).ConfigureAwait(false);

            return;
        }

        var parts = remainder.Split(
            ' ',
            2,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var action = parts[0].ToLowerInvariant();
        var argument = parts.Length > 1 ? parts[1] : null;

        switch (action)
        {
            case "current":
                PrintCurrentModel(state.CurrentProfile);
                return;

            case "use":
                if (string.IsNullOrWhiteSpace(argument))
                {
                    PrintWarningPanel("用法：/model use <name>");
                    return;
                }

                await UseModelAsync(
                    argument,
                    state,
                    exposedTools).ConfigureAwait(false);
                return;

            case "add":
                AddModelInteractive(state.Config, state.ConfigPath);
                return;

            case "edit":
                await EditModelInteractiveAsync(
                    state,
                    string.IsNullOrWhiteSpace(argument)
                        ? state.CurrentProfile.Name
                        : argument,
                    exposedTools).ConfigureAwait(false);
                return;

            case "remove":
                if (string.IsNullOrWhiteSpace(argument))
                {
                    PrintWarningPanel("用法：/model remove <name>");
                    return;
                }

                await RemoveModelAsync(
                    argument,
                    state,
                    exposedTools).ConfigureAwait(false);
                return;

            default:
                PrintWarningPanel(
                    "可用命令：/model、/model current、/model use <name>、/model add、/model edit <name>、/model remove <name>");
                return;
        }
    }

    private static async Task SelectModelInteractiveAsync(
        RuntimeState state,
        AITool[] exposedTools)
    {
        var choices = state.Config.Models.Profiles
            .Select(profile => profile.Name)
            .ToArray();

        var selected = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("选择要切换的模型")
                .PageSize(10)
                .AddChoices(choices));

        await UseModelAsync(
            selected,
            state,
            exposedTools).ConfigureAwait(false);
    }

    private static async Task UseModelAsync(
        string profileName,
        RuntimeState state,
        AITool[] exposedTools)
    {
        var profile = state.Config.Models.Profiles
            .FirstOrDefault(p => p.Name.Equals(profileName, StringComparison.OrdinalIgnoreCase));

        if (profile is null)
        {
            PrintWarningPanel($"找不到模型配置：{profileName}");
            return;
        }

        if (state.CurrentProfile.Name.Equals(profile.Name, StringComparison.OrdinalIgnoreCase))
        {
            PrintWarningPanel("当前已经在使用这个模型。");
            return;
        }

        try
        {
            state.Agent = CreateAgent(
                profile,
                state.Config.Safety,
                exposedTools);

            state.Session = await state.Agent.CreateSessionAsync().ConfigureAwait(false);
            state.SessionUsage.Reset();
            state.CurrentProfile = profile;
            state.Config.Models.Current = profile.Name;

            SaveConfig(state.Config, state.ConfigPath);

            PrintSuccessPanel(
                $"已切换到：{GetProfileDisplayName(profile)}。当前会话已重置。");
        }
        catch (Exception ex)
        {
            PrintErrorPanel($"切换失败：{ex.Message}");
        }
    }

    private static void AddModelInteractive(
        AppConfig config,
        string appConfigPath)
    {
        AnsiConsole.MarkupLine("[bold cyan]新增模型配置[/]");

        var name = PromptRequired("配置名");
        if (config.Models.Profiles.Any(p =>
                p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            PrintWarningPanel($"已存在同名配置：{name}");
            return;
        }

        var displayName = PromptOptional("显示名", name);
        var endpoint = PromptRequired("Endpoint，例如 http://localhost:1234/v1/");
        var model = PromptRequired("Model");
        var apiKey = PromptOptional("API Key（可留空，若使用环境变量）", "");
        var apiKeyEnv = PromptOptional("API Key 环境变量名（可留空）", "");

        var profile = new ModelProfile
        {
            Name = name,
            DisplayName = displayName,
            Endpoint = endpoint,
            Model = model,
            ApiKey = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey,
            ApiKeyEnvironmentVariable = string.IsNullOrWhiteSpace(apiKeyEnv) ? null : apiKeyEnv
        };

        try
        {
            ValidateModelProfile(profile);
            config.Models.Profiles.Add(profile);
            SaveConfig(config, appConfigPath);

            PrintSuccessPanel($"已新增模型配置：{name}");
        }
        catch (Exception ex)
        {
            PrintErrorPanel($"新增失败：{ex.Message}");
        }
    }

    private static async Task EditModelInteractiveAsync(
        RuntimeState state,
        string profileName,
        AITool[] exposedTools)
    {
        var profile = state.Config.Models.Profiles
            .FirstOrDefault(p => p.Name.Equals(profileName, StringComparison.OrdinalIgnoreCase));

        if (profile is null)
        {
            PrintWarningPanel($"找不到模型配置：{profileName}");
            return;
        }

        AnsiConsole.MarkupLine($"[bold cyan]编辑模型配置 · {Markup.Escape(profile.Name)}[/]");

        profile.DisplayName = PromptOptional("显示名", profile.DisplayName ?? profile.Name);
        profile.Endpoint = PromptOptional("Endpoint", profile.Endpoint);
        profile.Model = PromptOptional("Model", profile.Model);

        var apiKey = PromptOptional("API Key（留空保持原值）", "");
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            profile.ApiKey = apiKey;
        }

        var apiKeyEnv = PromptOptional(
            "API Key 环境变量名（留空保持原值，输入 - 清空）",
            "");

        if (apiKeyEnv == "-")
        {
            profile.ApiKeyEnvironmentVariable = null;
        }
        else if (!string.IsNullOrWhiteSpace(apiKeyEnv))
        {
            profile.ApiKeyEnvironmentVariable = apiKeyEnv;
        }

        try
        {
            ValidateModelProfile(profile);
            SaveConfig(state.Config, state.ConfigPath);

            if (state.CurrentProfile.Name.Equals(profile.Name, StringComparison.OrdinalIgnoreCase))
            {
                state.Agent = CreateAgent(
                    profile,
                    state.Config.Safety,
                    exposedTools);

                state.Session = await state.Agent.CreateSessionAsync().ConfigureAwait(false);
                state.SessionUsage.Reset();
                state.CurrentProfile = profile;

                PrintSuccessPanel("当前模型配置已更新，会话已重置。");
            }
            else
            {
                PrintSuccessPanel($"已更新模型配置：{profile.Name}");
            }
        }
        catch (Exception ex)
        {
            PrintErrorPanel($"更新失败：{ex.Message}");
        }
    }

    private static async Task RemoveModelAsync(
        string profileName,
        RuntimeState state,
        AITool[] exposedTools)
    {
        var profile = state.Config.Models.Profiles
            .FirstOrDefault(p => p.Name.Equals(profileName, StringComparison.OrdinalIgnoreCase));

        if (profile is null)
        {
            PrintWarningPanel($"找不到模型配置：{profileName}");
            return;
        }

        if (state.Config.Models.Profiles.Count == 1)
        {
            PrintWarningPanel("至少需要保留一个模型配置。");
            return;
        }

        state.Config.Models.Profiles.Remove(profile);

        var removedCurrent = state.CurrentProfile.Name.Equals(
            profile.Name,
            StringComparison.OrdinalIgnoreCase);

        if (!removedCurrent)
        {
            SaveConfig(state.Config, state.ConfigPath);
            PrintSuccessPanel($"已删除模型配置：{profile.Name}");
            return;
        }

        var next = state.Config.Models.Profiles[0];
        state.Config.Models.Current = next.Name;

        try
        {
            state.Agent = CreateAgent(
                next,
                state.Config.Safety,
                exposedTools);

            state.Session = await state.Agent.CreateSessionAsync().ConfigureAwait(false);
            state.SessionUsage.Reset();
            state.CurrentProfile = next;

            SaveConfig(state.Config, state.ConfigPath);

            PrintSuccessPanel(
                $"已删除当前模型，自动切换到：{GetProfileDisplayName(next)}。当前会话已重置。");
        }
        catch (Exception ex)
        {
            PrintErrorPanel($"删除后切换失败：{ex.Message}");
        }
    }

    private static async Task RunStreamingWithApprovalsAsync(
        AIAgent agent,
        string userInput,
        AgentSession session,
        TokenUsageAccumulator turnUsage)
    {
        ChatMessage? nextMessage = new(ChatRole.User, userInput);
        var firstRun = true;

        while (nextMessage is not null)
        {
            if (!firstRun)
            {
                ShowTransientStatus("DbAgent · 继续执行中…", "blue");
            }

            var approvalRequests = new List<ToolApprovalRequestContent>();

            await foreach (var update in agent.RunStreamingAsync(nextMessage, session))
            {
                UpdateTransientStatusFromStreamingUpdate(update);
                PrintStreamingUpdate(update);
                AccumulateUsage(update, turnUsage);
                CollectApprovalRequests(update, approvalRequests);
            }

            if (approvalRequests.Count == 0)
            {
                nextMessage = null;
                continue;
            }

            ClearTransientStatus();

            var approvalResponses = new List<AIContent>();

            foreach (var request in approvalRequests)
            {
                var toolName = GetToolName(request.ToolCall);
                var arguments = GetToolArguments(request.ToolCall);

                PrintApprovalPanel(toolName, arguments);

                var approved = ReadApprovalDecision();
                approvalResponses.Add(request.CreateResponse(approved));
            }

            nextMessage = new ChatMessage(
                ChatRole.User,
                approvalResponses);

            firstRun = false;
        }
    }

    private static void PrintStreamingUpdate(AgentResponseUpdate update)
    {
        if (!string.IsNullOrEmpty(update.Text))
        {
            EnsureAssistantHeader();
            Console.Write(update.Text);
            _hasStreamedText = true;
        }

        foreach (var content in update.Contents)
        {
            switch (content)
            {
                case TextContent:
                    break;

                case FunctionCallContent functionCall:
                    ClearTransientStatus();
                    PrintToolCallPanel(
                        functionCall.Name,
                        functionCall.Arguments);
                    break;

                case FunctionResultContent functionResult:
                    ClearTransientStatus();
                    PrintFunctionResultPanel(functionResult);
                    break;

                case ErrorContent error:
                    ClearTransientStatus();
                    PrintErrorPanel(error.Message);
                    break;

                case ToolApprovalRequestContent:
                    break;

                case UsageContent:
                    break;
            }
        }
    }

    private static void UpdateTransientStatusFromStreamingUpdate(AgentResponseUpdate update)
    {
        if (update.Contents.OfType<ToolApprovalRequestContent>().Any())
        {
            ShowTransientStatus("DbAgent · 等待审批…", "red");
            return;
        }

        if (update.Contents.OfType<FunctionCallContent>().Any())
        {
            ShowTransientStatus("DbAgent · 调用工具中…", "cyan");
            return;
        }

        if (!string.IsNullOrEmpty(update.Text))
        {
            ClearTransientStatus();
        }
    }

    private static void ResetTurnUiState()
    {
        _assistantHeaderPrinted = false;
        _hasStreamedText = false;
        _transientStatusText = null;
    }

    private static void EnsureAssistantHeader()
    {
        if (_assistantHeaderPrinted)
        {
            return;
        }

        ClearTransientStatus();
        AnsiConsole.MarkupLine("[bold green]DbAgent[/]");
        _assistantHeaderPrinted = true;
    }

    private static void ShowTransientStatus(string text, string color)
    {
        ClearTransientStatus();

        _transientStatusText = text;
        AnsiConsole.Markup($"[{color}]{Markup.Escape(text)}[/]");
    }

    private static void ClearTransientStatus()
    {
        if (string.IsNullOrEmpty(_transientStatusText))
        {
            return;
        }

        Console.Write("\r");
        Console.Write(new string(' ', 100));
        Console.Write("\r");
        _transientStatusText = null;
    }

    private static void PrintToolCallPanel(
        string toolName,
        IDictionary<string, object?>? arguments)
    {
        Console.WriteLine();

        var body = BuildArgumentsMarkup(arguments);

        var panel = new Panel(new Markup(body))
            .Header($"[bold cyan]tool · {Markup.Escape(toolName)}[/]")
            .Border(BoxBorder.Rounded)
            .BorderColor(Color.Cyan1)
            .Expand();

        AnsiConsole.Write(panel);
    }

    private static void PrintFunctionResultPanel(FunctionResultContent result)
    {
        Console.WriteLine();

        string body;

        if (result.Exception is not null)
        {
            body = $"[red]异常:[/] {Markup.Escape(result.Exception.Message)}";
        }
        else
        {
            body = Markup.Escape(
                Truncate(FormatValue(result.Result), 1200));
        }

        var panel = new Panel(new Markup(body))
            .Header("[bold grey]tool result[/]")
            .Border(BoxBorder.Rounded)
            .BorderColor(Color.Grey)
            .Expand();

        AnsiConsole.Write(panel);
    }

    private static void PrintApprovalPanel(
        string toolName,
        IDictionary<string, object?>? arguments)
    {
        Console.WriteLine();

        var body =
            "[bold red]该操作需要人工审批[/]\n\n" +
            BuildArgumentsMarkup(arguments);

        var panel = new Panel(new Markup(body))
            .Header($"[bold red]需要审批 · {Markup.Escape(toolName)}[/]")
            .Border(BoxBorder.Double)
            .BorderColor(Color.Red)
            .Expand();

        AnsiConsole.Write(panel);
    }

    private static bool ReadApprovalDecision()
    {
        while (true)
        {
            AnsiConsole.Markup("[bold yellow]批准执行？[/] [green]y[/] / [red]n[/] [grey]> [/]");
            var answer = Console.ReadLine()?.Trim();

            if (string.Equals(answer, "y", StringComparison.OrdinalIgnoreCase))
            {
                PrintSuccessPanel("已批准。");
                return true;
            }

            if (string.Equals(answer, "n", StringComparison.OrdinalIgnoreCase))
            {
                PrintWarningPanel("已拒绝。");
                return false;
            }

            PrintWarningPanel("请输入 y 或 n。");
        }
    }

    private static void CollectApprovalRequests(
        AgentResponseUpdate update,
        List<ToolApprovalRequestContent> approvalRequests)
    {
        approvalRequests.AddRange(
            update.Contents.OfType<ToolApprovalRequestContent>());
    }

    private static AITool WrapToolForApprovalIfNeeded(AITool tool)
    {
        if (tool is AIFunction function &&
            RequiresApproval(function.Name))
        {
            return new ApprovalRequiredAIFunction(function);
        }

        return tool;
    }

    private static bool RequiresApproval(string toolName)
    {
        return ApprovalRequiredTools.Contains(NormalizeToolName(toolName));
    }

    private static string GetToolName(ToolCallContent toolCall)
    {
        return toolCall switch
        {
            FunctionCallContent functionCall => functionCall.Name,
            _ => toolCall.GetType().Name
        };
    }

    private static IDictionary<string, object?>? GetToolArguments(ToolCallContent toolCall)
    {
        return toolCall switch
        {
            FunctionCallContent functionCall => functionCall.Arguments,
            _ => null
        };
    }

    private static string BuildArgumentsMarkup(
        IDictionary<string, object?>? arguments)
    {
        if (arguments is null || arguments.Count == 0)
        {
            return "[grey]无参数[/]";
        }

        return string.Join(
            Environment.NewLine,
            arguments.Select(arg =>
                $"[grey]{Markup.Escape(arg.Key)}[/]: {Markup.Escape(Truncate(FormatValue(arg.Value), 500))}"));
    }

    private static void AccumulateUsage(
        AgentResponseUpdate update,
        TokenUsageAccumulator accumulator)
    {
        foreach (var usage in update.Contents.OfType<UsageContent>())
        {
            var details = usage.Details;

            accumulator.HasUsage = true;
            accumulator.InputTokens += details.InputTokenCount ?? 0;
            accumulator.OutputTokens += details.OutputTokenCount ?? 0;
            accumulator.TotalTokens += details.TotalTokenCount
                ?? ((details.InputTokenCount ?? 0) + (details.OutputTokenCount ?? 0));
            accumulator.CachedInputTokens += details.CachedInputTokenCount ?? 0;
            accumulator.ReasoningTokens += details.ReasoningTokenCount ?? 0;
        }
    }

    private static void PrintCompactHeader(
        ModelProfile profile,
        SafetyConfig safety,
        int exposedToolCount,
        int totalToolCount)
    {
        AnsiConsole.MarkupLine(
            $"[bold cyan]DbAgent[/] [grey]·[/] " +
            $"[white]{Markup.Escape(GetProfileDisplayName(profile))}[/] [grey]·[/] " +
            $"[white]{Markup.Escape(profile.Model)}[/] [grey]·[/] " +
            $"{GetModeText(safety)} [grey]·[/] " +
            $"[white]{exposedToolCount}/{totalToolCount} tools[/]");
    }

    private static string GetModeText(SafetyConfig safety)
    {
        if (safety.AllowSchemaWriteTools)
        {
            return "[red]架构写入[/]";
        }

        if (safety.AllowWriteTools)
        {
            return "[yellow]受控写入[/]";
        }

        return "[green]只读[/]";
    }

    private static void PrintCompactTokenUsage(
        TokenUsageAccumulator turnUsage,
        TokenUsageAccumulator sessionUsage)
    {
        if (!turnUsage.HasUsage)
        {
            AnsiConsole.MarkupLine("[grey]usage unavailable[/]");
            return;
        }

        AnsiConsole.MarkupLine(
            $"[grey]ctx {turnUsage.InputTokens}  " +
            $"out {turnUsage.OutputTokens}  " +
            $"turn {turnUsage.TotalTokens}  " +
            $"session {sessionUsage.TotalTokens}[/]");
    }

    private static void PrintDetailedTokenUsage(TokenUsageAccumulator sessionUsage)
    {
        if (!sessionUsage.HasUsage)
        {
            PrintWarningPanel("当前会话还没有可显示的 token 统计。");
            return;
        }

        var table = new Table()
            .Border(TableBorder.None)
            .HideHeaders();

        table.AddColumn("项");
        table.AddColumn("值");

        table.AddRow("[grey]会话累计输入[/]", sessionUsage.InputTokens.ToString());
        table.AddRow("[grey]会话累计输出[/]", sessionUsage.OutputTokens.ToString());
        table.AddRow("[grey]会话累计总计[/]", sessionUsage.TotalTokens.ToString());

        if (sessionUsage.CachedInputTokens > 0)
        {
            table.AddRow("[grey]缓存输入[/]", sessionUsage.CachedInputTokens.ToString());
        }

        if (sessionUsage.ReasoningTokens > 0)
        {
            table.AddRow("[grey]推理 Token[/]", sessionUsage.ReasoningTokens.ToString());
        }

        var panel = new Panel(table)
            .Header("[bold cyan]统计[/]")
            .Border(BoxBorder.Rounded)
            .BorderColor(Color.Cyan1)
            .Expand();

        AnsiConsole.Write(panel);
    }

    private static void PrintModelProfiles(AppConfig config)
    {
        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Blue);

        table.AddColumn("[bold]当前[/]");
        table.AddColumn("[bold]配置名[/]");
        table.AddColumn("[bold]显示名[/]");
        table.AddColumn("[bold]模型[/]");
        table.AddColumn("[bold]Endpoint[/]");

        foreach (var profile in config.Models.Profiles)
        {
            var current = profile.Name.Equals(
                config.Models.Current,
                StringComparison.OrdinalIgnoreCase)
                ? "[green]*[/]"
                : "";

            table.AddRow(
                current,
                Markup.Escape(profile.Name),
                Markup.Escape(GetProfileDisplayName(profile)),
                Markup.Escape(profile.Model),
                Markup.Escape(profile.Endpoint));
        }

        var panel = new Panel(table)
            .Header("[bold blue]模型配置[/]")
            .Border(BoxBorder.Rounded)
            .BorderColor(Color.Blue)
            .Expand();

        AnsiConsole.Write(panel);
    }

    private static void PrintCurrentModel(ModelProfile profile)
    {
        var table = new Table()
            .Border(TableBorder.None)
            .HideHeaders();

        table.AddColumn("项");
        table.AddColumn("值");

        table.AddRow("[grey]配置名[/]", Markup.Escape(profile.Name));
        table.AddRow("[grey]显示名[/]", Markup.Escape(GetProfileDisplayName(profile)));
        table.AddRow("[grey]模型[/]", Markup.Escape(profile.Model));
        table.AddRow("[grey]Endpoint[/]", Markup.Escape(profile.Endpoint));
        table.AddRow("[grey]API Key 来源[/]", Markup.Escape(GetApiKeySource(profile)));

        var panel = new Panel(table)
            .Header("[bold blue]当前模型[/]")
            .Border(BoxBorder.Rounded)
            .BorderColor(Color.Blue)
            .Expand();

        AnsiConsole.Write(panel);
    }

    private static void PrintHelp()
    {
        var content = """
        [bold]可用命令[/]
        [cyan]/help[/]                查看帮助
        [cyan]/tools[/]               查看 MCP 工具
        [cyan]/config[/]              查看当前配置
        [cyan]/stats[/]               查看会话详细 token
        [cyan]/clear[/]               清空当前会话
        [cyan]/models[/]              查看所有模型配置
        [cyan]/model[/]               交互式切换模型
        [cyan]/model current[/]       查看当前模型
        [cyan]/model use <name>[/]    切换模型
        [cyan]/model add[/]           新增模型配置
        [cyan]/model edit <name>[/]   编辑模型配置
        [cyan]/model remove <name>[/] 删除模型配置
        [cyan]/exit[/]                退出

        [bold]示例问题[/]
        当前连接的是哪个数据库？
        列出所有表，并说明每张表可能的用途
        看一下 orders 表结构
        统计最近 30 天订单数和总金额
        找出销售额最高的 10 个客户
        把 id=10 的用户改成 VIP
        """;

        var panel = new Panel(new Markup(content))
            .Header("[bold blue]帮助[/]")
            .Border(BoxBorder.Rounded)
            .BorderColor(Color.Blue)
            .Expand();

        AnsiConsole.Write(panel);
    }

    private static void PrintCliHelp()
    {
        var content = """
        [bold]用法[/]
        [cyan]dbagent[/]                         启动交互式数据库 Agent
        [cyan]dbagent --model <name>[/]          使用指定模型配置启动
        [cyan]dbagent --config <path>[/]         使用指定配置文件启动
        [cyan]dbagent init[/]                    生成默认配置到 ~/.dbagent/
        [cyan]dbagent init --force[/]            覆盖默认配置
        [cyan]dbagent models[/]                  查看已配置模型
        [cyan]dbagent doctor[/]                  检查模型、MCP、数据库配置
        [cyan]dbagent doctor --model <name>[/]   检查指定模型配置
        [cyan]dbagent --help[/]                  查看帮助

        [bold]配置查找顺序[/]
        1. --config 指定路径
        2. 环境变量 DBAGENT_CONFIG
        3. 当前目录 appsettings.json
        4. ~/.dbagent/appsettings.json
        5. 程序目录 appsettings.json
        """;

        var panel = new Panel(new Markup(content))
            .Header("[bold cyan]DbAgent CLI[/]")
            .Border(BoxBorder.Rounded)
            .BorderColor(Color.Cyan1)
            .Expand();

        AnsiConsole.Write(panel);
    }

    private static void PrintTools(
        IEnumerable<string> allTools,
        IEnumerable<string> exposedTools)
    {
        var all = allTools
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var exposed = exposedTools
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Blue);

        table.AddColumn("[bold]状态[/]");
        table.AddColumn("[bold]工具[/]");

        foreach (var tool in all)
        {
            var status = exposed.Contains(tool)
                ? RequiresApproval(tool)
                    ? "[yellow]开放 / 审批[/]"
                    : "[green]开放[/]"
                : "[grey]屏蔽[/]";

            table.AddRow(status, Markup.Escape(tool));
        }

        var panel = new Panel(table)
            .Header($"[bold blue]MCP 工具 · {all.Length}[/]")
            .Border(BoxBorder.Rounded)
            .BorderColor(Color.Blue)
            .Expand();

        AnsiConsole.Write(panel);
    }

    private static void PrintConfig(
        AppConfig config,
        ModelProfile currentProfile,
        string appConfigPath,
        string databaseConfigPath)
    {
        var table = new Table()
            .Border(TableBorder.None)
            .HideHeaders();

        table.AddColumn("项");
        table.AddColumn("值");

        table.AddRow("[grey]配置文件[/]", Markup.Escape(appConfigPath));
        table.AddRow("[grey]当前模型配置[/]", Markup.Escape(currentProfile.Name));
        table.AddRow("[grey]模型名称[/]", Markup.Escape(currentProfile.Model));
        table.AddRow("[grey]模型地址[/]", Markup.Escape(currentProfile.Endpoint));
        table.AddRow("[grey]数据库配置[/]", Markup.Escape(databaseConfigPath));
        table.AddRow("[grey]MCP 命令[/]", Markup.Escape($"{config.Mcp.Command} {string.Join(' ', config.Mcp.Arguments)}"));
        table.AddRow("[grey]允许写入工具[/]", config.Safety.AllowWriteTools ? "[yellow]true[/]" : "[green]false[/]");
        table.AddRow("[grey]允许架构写入[/]", config.Safety.AllowSchemaWriteTools ? "[red]true[/]" : "[green]false[/]");

        var panel = new Panel(table)
            .Header("[bold blue]当前配置[/]")
            .Border(BoxBorder.Rounded)
            .BorderColor(Color.Blue)
            .Expand();

        AnsiConsole.Write(panel);
    }

    private static void PrintDivider()
    {
        AnsiConsole.Write(new Rule().RuleStyle("grey dim"));
    }

    private static void PrintSuccessPanel(string message)
    {
        var panel = new Panel(new Markup(Markup.Escape(message)))
            .Header("[bold green]成功[/]")
            .Border(BoxBorder.Rounded)
            .BorderColor(Color.Green)
            .Expand();

        AnsiConsole.Write(panel);
    }

    private static void PrintWarningPanel(string message)
    {
        var panel = new Panel(new Markup(Markup.Escape(message)))
            .Header("[bold yellow]提示[/]")
            .Border(BoxBorder.Rounded)
            .BorderColor(Color.Yellow)
            .Expand();

        AnsiConsole.Write(panel);
    }

    private static void PrintErrorPanel(string message)
    {
        Console.WriteLine();

        var panel = new Panel(new Markup(Markup.Escape(message)))
            .Header("[bold red]错误[/]")
            .Border(BoxBorder.Double)
            .BorderColor(Color.Red)
            .Expand();

        AnsiConsole.Write(panel);
    }

    private static string PromptRequired(string label)
    {
        while (true)
        {
            AnsiConsole.Markup($"[cyan]{Markup.Escape(label)}[/][grey]> [/]");
            var value = Console.ReadLine()?.Trim();

            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }

            PrintWarningPanel("不能为空。");
        }
    }

    private static string PromptOptional(string label, string defaultValue)
    {
        AnsiConsole.Markup(
            $"[cyan]{Markup.Escape(label)}[/] [grey](默认: {Markup.Escape(defaultValue)})[/][grey]> [/]");

        var value = Console.ReadLine()?.Trim();
        return string.IsNullOrWhiteSpace(value)
            ? defaultValue
            : value;
    }

    private static bool NormalizeConfig(AppConfig config)
    {
        var changed = false;

        if (config.Models.Profiles.Count == 0 && config.OpenAI is not null)
        {
            config.Models.Current = "default";
            config.Models.Profiles.Add(new ModelProfile
            {
                Name = "default",
                DisplayName = "默认模型",
                Endpoint = config.OpenAI.Endpoint,
                Model = config.OpenAI.Model,
                ApiKey = config.OpenAI.ApiKey
            });

            config.OpenAI = null;
            changed = true;
        }

        if (config.Models.Profiles.Count > 0 &&
            string.IsNullOrWhiteSpace(config.Models.Current))
        {
            config.Models.Current = config.Models.Profiles[0].Name;
            changed = true;
        }

        if (config.Models.Profiles.Count > 0 &&
            !config.Models.Profiles.Any(p =>
                p.Name.Equals(config.Models.Current, StringComparison.OrdinalIgnoreCase)))
        {
            config.Models.Current = config.Models.Profiles[0].Name;
            changed = true;
        }

        return changed;
    }

    private static AppConfig LoadConfig(string path)
    {
        var json = File.ReadAllText(path);

        return JsonSerializer.Deserialize<AppConfig>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        }) ?? throw new InvalidOperationException("appsettings.json 内容为空或格式不正确。");
    }

    private static void SaveConfig(AppConfig config, string path)
    {
        var json = JsonSerializer.Serialize(config, new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });

        File.WriteAllText(path, json);
    }

    private static string ResolvePath(string baseDirectory, string path)
    {
        return Path.IsPathRooted(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(Path.Combine(baseDirectory, path));
    }

    private static void ValidateConfig(AppConfig config, string databaseConfigPath)
    {
        if (config.Models.Profiles.Count == 0)
        {
            throw new InvalidOperationException("至少需要配置一个模型。");
        }

        foreach (var profile in config.Models.Profiles)
        {
            ValidateModelProfile(profile);
        }

        if (string.IsNullOrWhiteSpace(config.Models.Current))
        {
            throw new InvalidOperationException("Models.Current 不能为空。");
        }

        if (!config.Models.Profiles.Any(p =>
                p.Name.Equals(config.Models.Current, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"找不到当前模型配置：{config.Models.Current}");
        }

        if (config.Models.Profiles
            .GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .Any(g => g.Count() > 1))
        {
            throw new InvalidOperationException("模型配置名不能重复。");
        }

        if (string.IsNullOrWhiteSpace(config.Mcp.Command))
        {
            throw new InvalidOperationException("Mcp.Command 不能为空。");
        }

        if (!File.Exists(databaseConfigPath))
        {
            throw new FileNotFoundException($"找不到数据库配置文件: {databaseConfigPath}");
        }
    }

    private static void ValidateModelProfile(ModelProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.Name))
        {
            throw new InvalidOperationException("模型配置 Name 不能为空。");
        }

        if (string.IsNullOrWhiteSpace(profile.Endpoint))
        {
            throw new InvalidOperationException($"模型 {profile.Name} 的 Endpoint 不能为空。");
        }

        if (!Uri.TryCreate(profile.Endpoint, UriKind.Absolute, out _))
        {
            throw new InvalidOperationException(
                $"模型 {profile.Name} 的 Endpoint 必须是绝对 URL。");
        }

        if (string.IsNullOrWhiteSpace(profile.Model))
        {
            throw new InvalidOperationException($"模型 {profile.Name} 的 Model 不能为空。");
        }

        if (string.IsNullOrWhiteSpace(profile.ApiKey) &&
            string.IsNullOrWhiteSpace(profile.ApiKeyEnvironmentVariable))
        {
            throw new InvalidOperationException(
                $"模型 {profile.Name} 至少要配置 ApiKey 或 ApiKeyEnvironmentVariable。");
        }
    }

    private static ModelProfile GetStartupProfile(AppConfig config, string? modelName)
    {
        if (string.IsNullOrWhiteSpace(modelName))
        {
            return GetCurrentProfile(config);
        }

        return config.Models.Profiles.FirstOrDefault(p =>
                   p.Name.Equals(modelName, StringComparison.OrdinalIgnoreCase))
               ?? throw new InvalidOperationException($"找不到模型配置：{modelName}");
    }

    private static ModelProfile GetCurrentProfile(AppConfig config)
    {
        return config.Models.Profiles.First(p =>
            p.Name.Equals(config.Models.Current, StringComparison.OrdinalIgnoreCase));
    }

    private static string ResolveApiKey(ModelProfile profile)
    {
        if (!string.IsNullOrWhiteSpace(profile.ApiKeyEnvironmentVariable))
        {
            var value = Environment.GetEnvironmentVariable(profile.ApiKeyEnvironmentVariable);

            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }

            if (string.IsNullOrWhiteSpace(profile.ApiKey))
            {
                throw new InvalidOperationException(
                    $"找不到环境变量：{profile.ApiKeyEnvironmentVariable}");
            }
        }

        return profile.ApiKey!;
    }

    private static string GetProfileDisplayName(ModelProfile profile)
    {
        return string.IsNullOrWhiteSpace(profile.DisplayName)
            ? profile.Name
            : profile.DisplayName;
    }

    private static string GetApiKeySource(ModelProfile profile)
    {
        if (!string.IsNullOrWhiteSpace(profile.ApiKeyEnvironmentVariable))
        {
            return $"环境变量: {profile.ApiKeyEnvironmentVariable}";
        }

        return "配置文件";
    }

    private static bool IsAllowedTool(string toolName, SafetyConfig safety)
    {
        var normalized = NormalizeToolName(toolName);

        if (ReadOnlyTools.Contains(normalized))
        {
            return true;
        }

        if (safety.AllowWriteTools && WriteTools.Contains(normalized))
        {
            return true;
        }

        if (safety.AllowSchemaWriteTools && SchemaWriteTools.Contains(normalized))
        {
            return true;
        }

        return false;
    }

    private static string NormalizeToolName(string toolName)
    {
        return new string(toolName
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray());
    }

    private static string BuildInstructions(SafetyConfig safety)
    {
        var mode = safety.AllowWriteTools || safety.AllowSchemaWriteTools
            ? "受控写入模式"
            : "只读模式";

        var writeInstruction = safety.AllowWriteTools || safety.AllowSchemaWriteTools
            ? """
              当前允许使用部分写入类工具，但任何写入或结构变更都必须先清楚说明：
              1. 将执行什么操作；
              2. 影响哪些表或数据；
              3. 潜在风险是什么。
              在没有用户明确要求时，不要主动执行写入。
              """
            : """
              当前是只读模式。即使用户要求修改数据，也只能给出建议 SQL，不得执行写入、删除或 DDL。
              """;

        return $$"""
        你是一个数据库智能助手，当前处于{{mode}}。

        工作规则：
        1. 遇到陌生数据库时，先确认当前库，再查看相关表和字段，再作答。
        2. 先理解 schema，再生成 SQL，再执行查询，最后用中文解释结果。
        3. 默认优先做小范围查询；除非用户明确要求，否则查询结果最多返回 100 行。
        4. 统计类问题优先用 COUNT/SUM/AVG 等聚合查询，不要拉全表。
        5. 如果字段含义不清楚，先查表结构、主键和索引，再说明判断依据。
        6. 生成 SQL 时要尊重当前数据库方言，必要时先识别数据库类型。
        7. 不要泄露连接字符串、密钥或敏感配置。
        8. {{writeInstruction}}
        9. 控制台输出中不要使用 Markdown 标题、Markdown 粗体、Markdown 表格或 Markdown 列表符号。
        10. 回答尽量使用自然中文短段落，必要时使用普通编号。
        """;
    }

    private static void AddIfPresent(
        IDictionary<string, string?> target,
        string key,
        string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            target[key] = value;
        }
    }

    private static string FormatValue(object? value)
    {
        if (value is null)
        {
            return "(null)";
        }

        if (value is string text)
        {
            return text;
        }

        try
        {
            return JsonSerializer.Serialize(value, new JsonSerializerOptions
            {
                WriteIndented = false
            });
        }
        catch
        {
            return value.ToString() ?? "(null)";
        }
    }

    private static string Truncate(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
        {
            return text;
        }

        return text[..maxLength] + "...";
    }

    private const string DefaultAppSettingsJson = """
    {
      "Models": {
        "Current": "local-main",
        "Profiles": [
          {
            "Name": "local-main",
            "DisplayName": "本地主模型",
            "Endpoint": "http://localhost:1234/v1/",
            "Model": "your-model-name",
            "ApiKey": "local-key"
          }
        ]
      },
      "Database": {
        "ConfigPath": "databases.json"
      },
      "Mcp": {
        "Command": "DatabaseMcpServer",
        "Arguments": []
      },
      "Safety": {
        "AllowWriteTools": false,
        "AllowSchemaWriteTools": false
      }
    }
    """;

    private const string DefaultDatabasesJson = """
    {
      "databases": [
        {
          "name": "sqlite-local",
          "connectionString": "Data Source=./data/local.db;Cache=Shared;Mode=ReadWriteCreate;",
          "dbType": "Sqlite",
          "description": "本地 SQLite 数据库",
          "isDefault": true
        }
      ]
    }
    """;
}

public sealed record DoctorCheck(
    string Name,
    bool IsOk,
    string Detail);

public sealed class CliOptions
{
    public string Command { get; private set; } = "chat";
    public string? ConfigPath { get; private set; }
    public string? ModelName { get; private set; }
    public bool Force { get; private set; }
    public bool ShowHelp { get; private set; }

    public static CliOptions Parse(string[] args)
    {
        var result = new CliOptions();
        var queue = new Queue<string>(args);

        if (queue.Count > 0 && !queue.Peek().StartsWith("--", StringComparison.Ordinal))
        {
            result.Command = queue.Dequeue().Trim().ToLowerInvariant();
        }

        while (queue.Count > 0)
        {
            var arg = queue.Dequeue();

            switch (arg)
            {
                case "-h":
                case "--help":
                    result.ShowHelp = true;
                    break;

                case "--config":
                    result.ConfigPath = RequireValue(arg, queue);
                    break;

                case "--model":
                    result.ModelName = RequireValue(arg, queue);
                    break;

                case "--force":
                    result.Force = true;
                    break;

                default:
                    throw new InvalidOperationException($"未知参数：{arg}");
            }
        }

        return result;
    }

    private static string RequireValue(string option, Queue<string> queue)
    {
        if (queue.Count == 0)
        {
            throw new InvalidOperationException($"参数 {option} 需要一个值。");
        }

        return queue.Dequeue();
    }
}

public sealed class RuntimeState
{
    public required AppConfig Config { get; set; }
    public required ModelProfile CurrentProfile { get; set; }
    public required AIAgent Agent { get; set; }
    public required AgentSession Session { get; set; }
    public required TokenUsageAccumulator SessionUsage { get; set; }
    public required string ConfigPath { get; set; }
}

public sealed class AppConfig
{
    public ModelProfilesConfig Models { get; set; } = new();
    public DatabaseConfig Database { get; set; } = new();
    public McpConfig Mcp { get; set; } = new();
    public SafetyConfig Safety { get; set; } = new();

    [JsonPropertyName("OpenAI")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public LegacyOpenAIConfig? OpenAI { get; set; }
}

public sealed class ModelProfilesConfig
{
    public string Current { get; set; } = "";
    public List<ModelProfile> Profiles { get; set; } = [];
}

public sealed class ModelProfile
{
    public string Name { get; set; } = "";
    public string? DisplayName { get; set; }
    public string Endpoint { get; set; } = "";
    public string Model { get; set; } = "";
    public string? ApiKey { get; set; }
    public string? ApiKeyEnvironmentVariable { get; set; }
}

public sealed class LegacyOpenAIConfig
{
    public string Endpoint { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = "";
}

public sealed class DatabaseConfig
{
    public string ConfigPath { get; set; } = "databases.json";
    public string? SeqServerUrl { get; set; }
    public string? SeqApiKey { get; set; }
    public string? DdlWhitelist { get; set; }
}

public sealed class McpConfig
{
    public string Command { get; set; } = "DatabaseMcpServer";
    public string[] Arguments { get; set; } = [];
}

public sealed class SafetyConfig
{
    public bool AllowWriteTools { get; set; } = false;
    public bool AllowSchemaWriteTools { get; set; } = false;
}

public sealed class TokenUsageAccumulator
{
    public bool HasUsage { get; set; }
    public long InputTokens { get; set; }
    public long OutputTokens { get; set; }
    public long TotalTokens { get; set; }
    public long CachedInputTokens { get; set; }
    public long ReasoningTokens { get; set; }

    public void Add(TokenUsageAccumulator other)
    {
        HasUsage |= other.HasUsage;
        InputTokens += other.InputTokens;
        OutputTokens += other.OutputTokens;
        TotalTokens += other.TotalTokens;
        CachedInputTokens += other.CachedInputTokens;
        ReasoningTokens += other.ReasoningTokens;
    }

    public void Reset()
    {
        HasUsage = false;
        InputTokens = 0;
        OutputTokens = 0;
        TotalTokens = 0;
        CachedInputTokens = 0;
        ReasoningTokens = 0;
    }
}
