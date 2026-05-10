#pragma warning disable MEAI001

using System.ClientModel;
using DbAgent.Models;
using OpenAI;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;

namespace DbAgent.Services;

public sealed class DefaultAgentFactory : IAgentFactory
{
    private readonly IModelProfileService _modelProfileService;

    public DefaultAgentFactory(IModelProfileService modelProfileService)
    {
        _modelProfileService = modelProfileService;
    }

    public StdioClientTransport CreateTransport(AppConfig config, string databaseConfigPath)
    {
        return new StdioClientTransport(new()
        {
            Name = "DatabaseMcpServer",
            Command = config.Mcp.Command,
            Arguments = config.Mcp.Arguments,
            EnvironmentVariables = BuildMcpEnvironment(config, databaseConfigPath)
        });
    }

    public IChatClient CreateChatClient(ModelProfile profile)
    {
        _modelProfileService.ValidateModelProfile(profile);

        var apiKey = _modelProfileService.ResolveApiKey(profile);
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

    public AIAgent CreateAgent(ModelProfile profile, SafetyConfig safety, AITool[] tools)
    {
        var chatClient = CreateChatClient(profile);

        return chatClient.AsAIAgent(
            name: "DbAgent",
            instructions: BuildInstructions(safety),
            tools: tools);
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
}
