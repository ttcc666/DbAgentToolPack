using Microsoft.Agents.AI;
using DbAgent.Models;

namespace DbAgent.Services;

public interface IChatSessionRunner
{
    Task RunStreamingWithApprovalsAsync(
        AIAgent agent,
        string userInput,
        AgentSession session,
        TokenUsageAccumulator turnUsage,
        CancellationToken cancellationToken = default);
}
