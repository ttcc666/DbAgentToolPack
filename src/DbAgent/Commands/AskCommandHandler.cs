#pragma warning disable MEAI001

using DbAgent.Configuration;
using DbAgent.ConsoleUi;
using DbAgent.Models;
using DbAgent.Services;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;

namespace DbAgent.Commands;

public sealed class AskCommandHandler : ICommandHandler
{
    private readonly IConsoleUi _consoleUi;
    private readonly IAppConfigContextFactory _contextFactory;
    private readonly IAppConfigStore _appConfigStore;
    private readonly IModelProfileService _modelProfileService;
    private readonly IApprovalPolicy _approvalPolicy;
    private readonly IAgentFactory _agentFactory;

    public AskCommandHandler(
        IConsoleUi consoleUi,
        IAppConfigContextFactory contextFactory,
        IAppConfigStore appConfigStore,
        IModelProfileService modelProfileService,
        IApprovalPolicy approvalPolicy,
        IAgentFactory agentFactory)
    {
        _consoleUi = consoleUi;
        _contextFactory = contextFactory;
        _appConfigStore = appConfigStore;
        _modelProfileService = modelProfileService;
        _approvalPolicy = approvalPolicy;
        _agentFactory = agentFactory;
    }

    public string CommandName => "ask";

    public async Task<int> ExecuteAsync(CliOptions options, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(options.Query))
        {
            Console.Error.WriteLine("用法：dbagent ask \"你的问题\" [--model <name>] [--config <path>] [--yes]");
            return 2;
        }

        AppConfigContext context;

        try
        {
            context = _contextFactory.Create(options.ConfigPath);
        }
        catch (FileNotFoundException ex)
        {
            Console.Error.WriteLine($"错误：{ex.Message}");
            Console.Error.WriteLine("提示：首次使用可运行 dbagent init 生成配置。");
            return 2;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"读取配置失败：{ex.Message}");
            return 2;
        }

        try
        {
            _appConfigStore.Validate(context.Config, context.DatabaseConfigPath);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"配置无效：{ex.Message}");
            return 2;
        }

        ModelProfile selectedProfile;

        try
        {
            selectedProfile = _modelProfileService.GetStartupProfile(context.Config, options.ModelName);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"错误：{ex.Message}");
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

        var exposedTools = allMcpTools
            .Where(tool => _approvalPolicy.IsAllowed(tool.Name, context.Config.Safety))
            .Cast<AITool>()
            .Select(_approvalPolicy.WrapToolForApprovalIfNeeded)
            .ToArray();

        var agent = _agentFactory.CreateAgent(selectedProfile, context.Config.Safety, exposedTools);
        var session = await agent.CreateSessionAsync().ConfigureAwait(false);

        try
        {
            return await RunSingleQueryAsync(agent, session, options, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"执行失败：{ex.Message}");
            return 2;
        }
    }

    private async Task<int> RunSingleQueryAsync(
        AIAgent agent,
        AgentSession session,
        CliOptions options,
        CancellationToken cancellationToken)
    {
        ChatMessage? nextMessage = new(ChatRole.User, options.Query!);

        while (nextMessage is not null)
        {
            var approvalRequests = new List<ToolApprovalRequestContent>();

            await foreach (var update in agent
                .RunStreamingAsync(nextMessage, session)
                .WithCancellation(cancellationToken))
            {
                if (!string.IsNullOrEmpty(update.Text))
                    Console.Write(update.Text);

                foreach (var content in update.Contents)
                {
                    switch (content)
                    {
                        case FunctionCallContent fc:
                            Console.Error.WriteLine($"[工具] {fc.Name}");
                            break;
                        case FunctionResultContent:
                            Console.Error.WriteLine("[结果] 已获取");
                            break;
                        case ToolApprovalRequestContent tar:
                            approvalRequests.Add(tar);
                            break;
                    }
                }
            }

            if (approvalRequests.Count == 0)
            {
                nextMessage = null;
                continue;
            }

            var responses = new List<AIContent>();

            foreach (var req in approvalRequests)
            {
                var toolName = GetToolName(req.ToolCall);

                if (options.AutoApprove)
                    Console.Error.WriteLine($"[批准] {toolName}（--yes）");
                else
                    Console.Error.WriteLine($"[拒绝] {toolName}（需要 --yes 才能自动批准写入操作）");

                responses.Add(req.CreateResponse(options.AutoApprove));
            }

            nextMessage = new ChatMessage(ChatRole.User, responses);
        }

        Console.WriteLine();
        return 0;
    }

    private static string GetToolName(ToolCallContent toolCall) =>
        toolCall switch
        {
            FunctionCallContent fc => fc.Name,
            _ => toolCall.GetType().Name
        };
}
