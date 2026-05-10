#pragma warning disable MEAI001

using DbAgent.Configuration;
using DbAgent.ConsoleUi;
using DbAgent.Models;
using DbAgent.Services;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;
using Spectre.Console;

namespace DbAgent.Commands;

public sealed class ChatCommandHandler : ICommandHandler
{
    private readonly IConsoleUi _consoleUi;
    private readonly IAppConfigContextFactory _contextFactory;
    private readonly IAppConfigStore _appConfigStore;
    private readonly IModelProfileService _modelProfileService;
    private readonly IApprovalPolicy _approvalPolicy;
    private readonly IAgentFactory _agentFactory;
    private readonly IChatSessionRunner _chatSessionRunner;

    public ChatCommandHandler(
        IConsoleUi consoleUi,
        IAppConfigContextFactory contextFactory,
        IAppConfigStore appConfigStore,
        IModelProfileService modelProfileService,
        IApprovalPolicy approvalPolicy,
        IAgentFactory agentFactory,
        IChatSessionRunner chatSessionRunner)
    {
        _consoleUi = consoleUi;
        _contextFactory = contextFactory;
        _appConfigStore = appConfigStore;
        _modelProfileService = modelProfileService;
        _approvalPolicy = approvalPolicy;
        _agentFactory = agentFactory;
        _chatSessionRunner = chatSessionRunner;
    }

    public string CommandName => "chat";

    public async Task<int> ExecuteAsync(CliOptions options, CancellationToken cancellationToken = default)
    {
        AppConfigContext context;

        try
        {
            context = _contextFactory.Create(options.ConfigPath);
        }
        catch (FileNotFoundException ex)
        {
            _consoleUi.PrintErrorPanel(ex.Message);
            _consoleUi.MarkupLine("[grey]提示：首次使用可运行 [white]dbagent init[/] 生成配置。[/]");
            return 2;
        }
        catch (Exception ex)
        {
            _consoleUi.PrintErrorPanel($"读取配置失败：{ex.Message}");
            return 2;
        }

        try
        {
            _appConfigStore.Validate(context.Config, context.DatabaseConfigPath);
        }
        catch (Exception ex)
        {
            _consoleUi.PrintErrorPanel($"配置无效：{ex.Message}");
            return 2;
        }

        ModelProfile selectedProfile;

        try
        {
            selectedProfile = _modelProfileService.GetStartupProfile(context.Config, options.ModelName);
        }
        catch (Exception ex)
        {
            _consoleUi.PrintErrorPanel(ex.Message);
            return 2;
        }

        await using var mcpClient = await McpClient
            .CreateAsync(
                _agentFactory.CreateTransport(context.Config, context.DatabaseConfigPath),
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var allMcpTools = await mcpClient
            .ListToolsAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var exposedMcpTools = allMcpTools
            .Where(tool => _approvalPolicy.IsAllowed(tool.Name, context.Config.Safety))
            .ToArray();

        var exposedTools = exposedMcpTools
            .Cast<AITool>()
            .Select(_approvalPolicy.WrapToolForApprovalIfNeeded)
            .ToArray();

        var agent = _agentFactory.CreateAgent(
            selectedProfile,
            context.Config.Safety,
            exposedTools);

        var session = await agent.CreateSessionAsync().ConfigureAwait(false);

        var state = new ChatRuntimeState
        {
            Config = context.Config,
            ConfigPath = context.ConfigPath,
            DatabaseConfigPath = context.DatabaseConfigPath,
            CurrentProfile = selectedProfile,
            Agent = agent,
            Session = session,
            SessionUsage = new TokenUsageAccumulator(),
            AllToolNames = allMcpTools.Select(tool => tool.Name).ToArray(),
            ExposedToolNames = exposedMcpTools.Select(tool => tool.Name).ToArray(),
            ExposedTools = exposedTools
        };

        _consoleUi.Clear();
        _consoleUi.PrintCompactHeader(
            state.CurrentProfile,
            state.Config.Safety,
            state.ExposedTools.Length,
            state.AllToolNames.Count,
            _modelProfileService.GetProfileDisplayName);

        if (state.ExposedTools.Length == 0)
        {
            _consoleUi.ShowNoToolsWarning();
        }

        _consoleUi.MarkupLine("[grey]输入 [white]/help[/] 查看命令。[/]");

        while (true)
        {
            var input = _consoleUi.ReadChatInput();

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
                _consoleUi.PrintHelp();
                continue;
            }

            if (trimmed.Equals("/tools", StringComparison.OrdinalIgnoreCase))
            {
                _consoleUi.PrintTools(
                    state.AllToolNames,
                    state.ExposedToolNames,
                    _approvalPolicy.RequiresApproval);
                continue;
            }

            if (trimmed.Equals("/config", StringComparison.OrdinalIgnoreCase))
            {
                _consoleUi.PrintConfig(
                    state.Config,
                    state.CurrentProfile,
                    state.ConfigPath,
                    state.DatabaseConfigPath);
                continue;
            }

            if (trimmed.Equals("/stats", StringComparison.OrdinalIgnoreCase))
            {
                _consoleUi.PrintDetailedTokenUsage(state.SessionUsage);
                continue;
            }

            if (trimmed.Equals("/clear", StringComparison.OrdinalIgnoreCase))
            {
                state.Session = await state.Agent.CreateSessionAsync().ConfigureAwait(false);
                state.SessionUsage.Reset();
                _consoleUi.PrintSuccessPanel("会话已清空。");
                continue;
            }

            if (trimmed.Equals("/models", StringComparison.OrdinalIgnoreCase))
            {
                _consoleUi.PrintModelProfiles(state.Config, _modelProfileService.GetProfileDisplayName);
                continue;
            }

            if (trimmed.Equals("/model", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("/model ", StringComparison.OrdinalIgnoreCase))
            {
                await HandleModelCommandAsync(trimmed, state).ConfigureAwait(false);
                continue;
            }

            var turnUsage = new TokenUsageAccumulator();

            try
            {
                _consoleUi.ResetTurnUiState();
                _consoleUi.ShowThinkingStatus();

                await _chatSessionRunner.RunStreamingWithApprovalsAsync(
                    state.Agent,
                    trimmed,
                    state.Session,
                    turnUsage,
                    cancellationToken).ConfigureAwait(false);

                state.SessionUsage.Add(turnUsage);
                _consoleUi.PrintCompactTokenUsage(turnUsage, state.SessionUsage);
            }
            catch (Exception ex)
            {
                _consoleUi.PrintErrorPanel($"执行失败: {ex.Message}");
            }
        }
    }

    private async Task HandleModelCommandAsync(string command, ChatRuntimeState state)
    {
        var remainder = command.Length == "/model".Length
            ? ""
            : command["/model".Length..].Trim();

        if (string.IsNullOrWhiteSpace(remainder))
        {
            await SelectModelInteractiveAsync(state).ConfigureAwait(false);
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
                _consoleUi.PrintCurrentModel(
                    state.CurrentProfile,
                    _modelProfileService.GetProfileDisplayName,
                    _modelProfileService.GetApiKeySource);
                return;

            case "use":
                if (string.IsNullOrWhiteSpace(argument))
                {
                    _consoleUi.PrintWarningPanel("用法：/model use <name>");
                    return;
                }

                await UseModelAsync(argument, state).ConfigureAwait(false);
                return;

            case "add":
                AddModelInteractive(state);
                return;

            case "edit":
                await EditModelInteractiveAsync(
                    state,
                    string.IsNullOrWhiteSpace(argument)
                        ? state.CurrentProfile.Name
                        : argument).ConfigureAwait(false);
                return;

            case "remove":
                if (string.IsNullOrWhiteSpace(argument))
                {
                    _consoleUi.PrintWarningPanel("用法：/model remove <name>");
                    return;
                }

                await RemoveModelAsync(argument, state).ConfigureAwait(false);
                return;

            default:
                _consoleUi.PrintWarningPanel(
                    "可用命令：/model、/model current、/model use <name>、/model add、/model edit <name>、/model remove <name>");
                return;
        }
    }

    private async Task SelectModelInteractiveAsync(ChatRuntimeState state)
    {
        var selected = _consoleUi.PromptSelection(
            "选择要切换的模型",
            state.Config.Models.Profiles.Select(profile => profile.Name));

        await UseModelAsync(selected, state).ConfigureAwait(false);
    }

    private async Task UseModelAsync(string profileName, ChatRuntimeState state)
    {
        var profile = _modelProfileService.FindProfile(state.Config, profileName);

        if (profile is null)
        {
            _consoleUi.PrintWarningPanel($"找不到模型配置：{profileName}");
            return;
        }

        if (state.CurrentProfile.Name.Equals(profile.Name, StringComparison.OrdinalIgnoreCase))
        {
            _consoleUi.PrintWarningPanel("当前已经在使用这个模型。");
            return;
        }

        try
        {
            state.Agent = _agentFactory.CreateAgent(
                profile,
                state.Config.Safety,
                state.ExposedTools);
            state.Session = await state.Agent.CreateSessionAsync().ConfigureAwait(false);
            state.SessionUsage.Reset();
            state.CurrentProfile = profile;
            state.Config.Models.Current = profile.Name;

            _appConfigStore.Save(state.Config, state.ConfigPath);

            _consoleUi.PrintSuccessPanel(
                $"已切换到：{_modelProfileService.GetProfileDisplayName(profile)}。当前会话已重置。");
        }
        catch (Exception ex)
        {
            _consoleUi.PrintErrorPanel($"切换失败：{ex.Message}");
        }
    }

    private void AddModelInteractive(ChatRuntimeState state)
    {
        _consoleUi.MarkupLine("[bold cyan]新增模型配置[/]");

        var name = _consoleUi.PromptRequired("配置名");

        if (_modelProfileService.FindProfile(state.Config, name) is not null)
        {
            _consoleUi.PrintWarningPanel($"已存在同名配置：{name}");
            return;
        }

        var displayName = _consoleUi.PromptOptional("显示名", name);
        var endpoint = _consoleUi.PromptRequired("Endpoint，例如 http://localhost:1234/v1/");
        var model = _consoleUi.PromptRequired("Model");
        var apiKey = _consoleUi.PromptOptional("API Key（可留空，若使用环境变量）", "");
        var apiKeyEnv = _consoleUi.PromptOptional("API Key 环境变量名（可留空）", "");

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
            _modelProfileService.AddProfile(state.Config, profile);
            _appConfigStore.Save(state.Config, state.ConfigPath);
            _consoleUi.PrintSuccessPanel($"已新增模型配置：{name}");
        }
        catch (Exception ex)
        {
            _consoleUi.PrintErrorPanel($"新增失败：{ex.Message}");
        }
    }

    private async Task EditModelInteractiveAsync(ChatRuntimeState state, string profileName)
    {
        var profile = _modelProfileService.FindProfile(state.Config, profileName);

        if (profile is null)
        {
            _consoleUi.PrintWarningPanel($"找不到模型配置：{profileName}");
            return;
        }

        _consoleUi.MarkupLine($"[bold cyan]编辑模型配置 · {Markup.Escape(profile.Name)}[/]");

        profile.DisplayName = _consoleUi.PromptOptional("显示名", profile.DisplayName ?? profile.Name);
        profile.Endpoint = _consoleUi.PromptOptional("Endpoint", profile.Endpoint);
        profile.Model = _consoleUi.PromptOptional("Model", profile.Model);

        var apiKey = _consoleUi.PromptOptional("API Key（留空保持原值）", "");
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            profile.ApiKey = apiKey;
        }

        var apiKeyEnv = _consoleUi.PromptOptional(
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
            _modelProfileService.ValidateModelProfile(profile);
            _appConfigStore.Save(state.Config, state.ConfigPath);

            if (state.CurrentProfile.Name.Equals(profile.Name, StringComparison.OrdinalIgnoreCase))
            {
                state.Agent = _agentFactory.CreateAgent(
                    profile,
                    state.Config.Safety,
                    state.ExposedTools);
                state.Session = await state.Agent.CreateSessionAsync().ConfigureAwait(false);
                state.SessionUsage.Reset();
                state.CurrentProfile = profile;
                _consoleUi.PrintSuccessPanel("当前模型配置已更新，会话已重置。");
            }
            else
            {
                _consoleUi.PrintSuccessPanel($"已更新模型配置：{profile.Name}");
            }
        }
        catch (Exception ex)
        {
            _consoleUi.PrintErrorPanel($"更新失败：{ex.Message}");
        }
    }

    private async Task RemoveModelAsync(string profileName, ChatRuntimeState state)
    {
        try
        {
            var result = _modelProfileService.RemoveProfile(
                state.Config,
                profileName,
                state.CurrentProfile.Name);

            if (!result.RemovedCurrentProfile)
            {
                _appConfigStore.Save(state.Config, state.ConfigPath);
                _consoleUi.PrintSuccessPanel($"已删除模型配置：{result.RemovedProfile.Name}");
                return;
            }

            var nextProfile = result.NextCurrentProfile!;
            state.Agent = _agentFactory.CreateAgent(
                nextProfile,
                state.Config.Safety,
                state.ExposedTools);
            state.Session = await state.Agent.CreateSessionAsync().ConfigureAwait(false);
            state.SessionUsage.Reset();
            state.CurrentProfile = nextProfile;

            _appConfigStore.Save(state.Config, state.ConfigPath);
            _consoleUi.PrintSuccessPanel(
                $"已删除当前模型，自动切换到：{_modelProfileService.GetProfileDisplayName(nextProfile)}。当前会话已重置。");
        }
        catch (InvalidOperationException ex)
        {
            _consoleUi.PrintWarningPanel(ex.Message);
        }
        catch (Exception ex)
        {
            _consoleUi.PrintErrorPanel($"删除后切换失败：{ex.Message}");
        }
    }
}
