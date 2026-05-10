using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace DbAgent.Models;

public sealed class ChatRuntimeState
{
    public required AppConfig Config { get; init; }
    public required string ConfigPath { get; init; }
    public required string DatabaseConfigPath { get; init; }
    public required IList<string> AllToolNames { get; init; }
    public required IList<string> ExposedToolNames { get; init; }
    public required AITool[] ExposedTools { get; set; }
    public required ModelProfile CurrentProfile { get; set; }
    public required AIAgent Agent { get; set; }
    public required AgentSession Session { get; set; }
    public required TokenUsageAccumulator SessionUsage { get; init; }
}
