using System.Diagnostics;
using System.Text.Json;
using DbAgent.Configuration;
using DbAgent.Models;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;

namespace DbAgent.Services;

public sealed class DoctorService : IDoctorService
{
    private readonly IModelProfileService _modelProfileService;
    private readonly IAgentFactory _agentFactory;

    public DoctorService(
        IModelProfileService modelProfileService,
        IAgentFactory agentFactory)
    {
        _modelProfileService = modelProfileService;
        _agentFactory = agentFactory;
    }

    public async Task<DoctorReport> RunAsync(
        AppConfig config,
        string appConfigPath,
        string? modelName,
        CancellationToken cancellationToken = default)
    {
        var checks = new List<DoctorCheck>();
        var configDirectory = Path.GetDirectoryName(appConfigPath)!;
        var databaseConfigPath = PathUtility.ResolvePath(
            configDirectory,
            config.Database.ConfigPath);

        checks.Add(new DoctorCheck("应用配置", true, appConfigPath));

        ModelProfile? profile = null;

        try
        {
            profile = _modelProfileService.GetStartupProfile(config, modelName);
            _modelProfileService.ValidateModelProfile(profile);
            _ = _modelProfileService.ResolveApiKey(profile);

            checks.Add(new DoctorCheck(
                "模型配置",
                true,
                $"{profile.Name} · {profile.Model}"));
        }
        catch (Exception ex)
        {
            checks.Add(new DoctorCheck("模型配置", false, ex.Message));
        }

        if (profile is not null)
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(TimeSpan.FromSeconds(20));

                var stopwatch = Stopwatch.StartNew();
                var chatClient = _agentFactory.CreateChatClient(profile);
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
                checks.Add(new DoctorCheck("模型连通性", false, ex.Message));
            }
        }

        var databaseConfigInspection = InspectDatabaseConfigFile(databaseConfigPath);
        checks.Add(new DoctorCheck(
            "数据库配置文件",
            databaseConfigInspection.IsOk,
            databaseConfigInspection.Detail));

        if (!databaseConfigInspection.IsOk)
        {
            return new DoctorReport { Checks = checks };
        }

        try
        {
            await using var mcpClient = await McpClient
                .CreateAsync(
                    _agentFactory.CreateTransport(config, databaseConfigPath),
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(30));

            var tools = await mcpClient
                .ListToolsAsync(cancellationToken: cts.Token)
                .ConfigureAwait(false);

            checks.Add(new DoctorCheck(
                "MCP 启动",
                true,
                $"已连接，发现 {tools.Count} 个工具"));

            var validateConfigurationTool = FindToolName(
                tools.Select(tool => tool.Name),
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
                tools.Select(tool => tool.Name),
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
            checks.Add(new DoctorCheck("MCP 启动", false, ex.Message));
        }

        return new DoctorReport { Checks = checks };
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

    private static string NormalizeToolName(string toolName)
    {
        return new string(toolName
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray());
    }
}
