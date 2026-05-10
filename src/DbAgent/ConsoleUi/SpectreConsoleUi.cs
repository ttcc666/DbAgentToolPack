using DbAgent.Models;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Spectre.Console;

namespace DbAgent.ConsoleUi;

public sealed class SpectreConsoleUi : IConsoleUi
{
    private bool _assistantHeaderPrinted;
    private bool _hasStreamedText;
    private string? _transientStatusText;

    public void Clear()
    {
        AnsiConsole.Clear();
    }

    public void MarkupLine(string markup)
    {
        AnsiConsole.MarkupLine(markup);
    }

    public void PrintDivider()
    {
        AnsiConsole.Write(new Rule().RuleStyle("grey dim"));
    }

    public string? ReadChatInput()
    {
        AnsiConsole.WriteLine();
        PrintDivider();
        AnsiConsole.Markup("[bold blue]你[/][grey]> [/]");
        return Console.ReadLine();
    }

    public string PromptRequired(string label)
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

    public string PromptOptional(string label, string defaultValue)
    {
        AnsiConsole.Markup(
            $"[cyan]{Markup.Escape(label)}[/] [grey](默认: {Markup.Escape(defaultValue)})[/][grey]> [/]");

        var value = Console.ReadLine()?.Trim();
        return string.IsNullOrWhiteSpace(value) ? defaultValue : value;
    }

    public string PromptSelection(string title, IEnumerable<string> choices)
    {
        return AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title(title)
                .PageSize(10)
                .AddChoices(choices));
    }

    public void PrintCompactHeader(
        ModelProfile profile,
        SafetyConfig safety,
        int exposedToolCount,
        int totalToolCount,
        Func<ModelProfile, string> displayNameAccessor)
    {
        AnsiConsole.MarkupLine(
            $"[bold cyan]DbAgent[/] [grey]·[/] " +
            $"[white]{Markup.Escape(displayNameAccessor(profile))}[/] [grey]·[/] " +
            $"[white]{Markup.Escape(profile.Model)}[/] [grey]·[/] " +
            $"{GetModeText(safety)} [grey]·[/] " +
            $"[white]{exposedToolCount}/{totalToolCount} tools[/]");
    }

    public void PrintCliHelp()
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

    public void PrintHelp()
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

    public void PrintTools(
        IEnumerable<string> allTools,
        IEnumerable<string> exposedTools,
        Func<string, bool> requiresApproval)
    {
        var all = allTools
            .OrderBy(tool => tool, StringComparer.OrdinalIgnoreCase)
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
                ? requiresApproval(tool)
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

    public void PrintConfig(
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

    public void PrintModelProfiles(AppConfig config, Func<ModelProfile, string> displayNameAccessor)
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
                Markup.Escape(displayNameAccessor(profile)),
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

    public void PrintCurrentModel(
        ModelProfile profile,
        Func<ModelProfile, string> displayNameAccessor,
        Func<ModelProfile, string> apiKeySourceAccessor)
    {
        var table = new Table()
            .Border(TableBorder.None)
            .HideHeaders();

        table.AddColumn("项");
        table.AddColumn("值");

        table.AddRow("[grey]配置名[/]", Markup.Escape(profile.Name));
        table.AddRow("[grey]显示名[/]", Markup.Escape(displayNameAccessor(profile)));
        table.AddRow("[grey]模型[/]", Markup.Escape(profile.Model));
        table.AddRow("[grey]Endpoint[/]", Markup.Escape(profile.Endpoint));
        table.AddRow("[grey]API Key 来源[/]", Markup.Escape(apiKeySourceAccessor(profile)));

        var panel = new Panel(table)
            .Header("[bold blue]当前模型[/]")
            .Border(BoxBorder.Rounded)
            .BorderColor(Color.Blue)
            .Expand();

        AnsiConsole.Write(panel);
    }

    public void PrintCompactTokenUsage(TokenUsageAccumulator turnUsage, TokenUsageAccumulator sessionUsage)
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

    public void PrintDetailedTokenUsage(TokenUsageAccumulator sessionUsage)
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

    public void PrintDoctorReport(IEnumerable<DoctorCheck> checks)
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

        if (items.All(check => check.IsOk))
        {
            PrintSuccessPanel("所有检查均通过。");
        }
        else
        {
            PrintWarningPanel("存在未通过的检查。");
        }
    }

    public void PrintSuccessPanel(string message)
    {
        var panel = new Panel(new Markup(Markup.Escape(message)))
            .Header("[bold green]成功[/]")
            .Border(BoxBorder.Rounded)
            .BorderColor(Color.Green)
            .Expand();

        AnsiConsole.Write(panel);
    }

    public void PrintWarningPanel(string message)
    {
        var panel = new Panel(new Markup(Markup.Escape(message)))
            .Header("[bold yellow]提示[/]")
            .Border(BoxBorder.Rounded)
            .BorderColor(Color.Yellow)
            .Expand();

        AnsiConsole.Write(panel);
    }

    public void PrintErrorPanel(string message)
    {
        Console.WriteLine();

        var panel = new Panel(new Markup(Markup.Escape(message)))
            .Header("[bold red]错误[/]")
            .Border(BoxBorder.Double)
            .BorderColor(Color.Red)
            .Expand();

        AnsiConsole.Write(panel);
    }

    public void ShowNoToolsWarning()
    {
        PrintWarningPanel("没有暴露任何 MCP 工具。请检查 DatabaseMcpServer 是否正确启动，以及工具名称是否匹配。");
    }

    public void ResetTurnUiState()
    {
        _assistantHeaderPrinted = false;
        _hasStreamedText = false;
        _transientStatusText = null;
    }

    public void ShowThinkingStatus()
    {
        ShowTransientStatus("DbAgent · 思考中…", "yellow");
    }

    public void ShowContinueExecutionStatus()
    {
        ShowTransientStatus("DbAgent · 继续执行中…", "blue");
    }

    public void HandleStreamingUpdate(AgentResponseUpdate update)
    {
        UpdateTransientStatus(update);

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
                    PrintToolCallPanel(functionCall.Name, functionCall.Arguments);
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
                case UsageContent:
                    break;
            }
        }
    }

    public void CompleteStreamingTurn()
    {
        ClearTransientStatus();

        if (_hasStreamedText)
        {
            Console.WriteLine();
        }
    }

    public bool AskForApproval(string toolName, IDictionary<string, object?>? arguments)
    {
        ClearTransientStatus();
        PrintApprovalPanel(toolName, arguments);

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

    private void UpdateTransientStatus(AgentResponseUpdate update)
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

    private void EnsureAssistantHeader()
    {
        if (_assistantHeaderPrinted)
        {
            return;
        }

        ClearTransientStatus();
        AnsiConsole.MarkupLine("[bold green]DbAgent[/]");
        _assistantHeaderPrinted = true;
    }

    private void ShowTransientStatus(string text, string color)
    {
        ClearTransientStatus();

        _transientStatusText = text;
        AnsiConsole.Markup($"[{color}]{Markup.Escape(text)}[/]");
    }

    private void ClearTransientStatus()
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

    private void PrintToolCallPanel(
        string toolName,
        IDictionary<string, object?>? arguments)
    {
        Console.WriteLine();

        var panel = new Panel(new Markup(BuildArgumentsMarkup(arguments)))
            .Header($"[bold cyan]tool · {Markup.Escape(toolName)}[/]")
            .Border(BoxBorder.Rounded)
            .BorderColor(Color.Cyan1)
            .Expand();

        AnsiConsole.Write(panel);
    }

    private void PrintFunctionResultPanel(FunctionResultContent result)
    {
        Console.WriteLine();

        var body = result.Exception is not null
            ? $"[red]异常:[/] {Markup.Escape(result.Exception.Message)}"
            : Markup.Escape(Truncate(FormatValue(result.Result), 1200));

        var panel = new Panel(new Markup(body))
            .Header("[bold grey]tool result[/]")
            .Border(BoxBorder.Rounded)
            .BorderColor(Color.Grey)
            .Expand();

        AnsiConsole.Write(panel);
    }

    private void PrintApprovalPanel(
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

    private static string BuildArgumentsMarkup(IDictionary<string, object?>? arguments)
    {
        if (arguments is null || arguments.Count == 0)
        {
            return "[grey]无参数[/]";
        }

        return string.Join(
            global::System.Environment.NewLine,
            arguments.Select(argument =>
                $"[grey]{Markup.Escape(argument.Key)}[/]: {Markup.Escape(Truncate(FormatValue(argument.Value), 500))}"));
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
            return System.Text.Json.JsonSerializer.Serialize(value, new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true
            });
        }
        catch
        {
            return value.ToString() ?? "(unknown)";
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
}
