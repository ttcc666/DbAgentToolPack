using DbAgent.ConsoleUi;
using DbAgent.Models;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace DbAgent.Services;

public sealed class ChatSessionRunner : IChatSessionRunner
{
    private readonly IConsoleUi _consoleUi;

    public ChatSessionRunner(IConsoleUi consoleUi)
    {
        _consoleUi = consoleUi;
    }

    public async Task RunStreamingWithApprovalsAsync(
        AIAgent agent,
        string userInput,
        AgentSession session,
        TokenUsageAccumulator turnUsage,
        CancellationToken cancellationToken = default)
    {
        ChatMessage? nextMessage = new(ChatRole.User, userInput);
        var firstRun = true;

        while (nextMessage is not null)
        {
            if (!firstRun)
            {
                _consoleUi.ShowContinueExecutionStatus();
            }

            var approvalRequests = new List<ToolApprovalRequestContent>();

            await foreach (var update in agent
                .RunStreamingAsync(nextMessage, session)
                .WithCancellation(cancellationToken))
            {
                _consoleUi.HandleStreamingUpdate(update);
                AccumulateUsage(update, turnUsage);
                approvalRequests.AddRange(update.Contents.OfType<ToolApprovalRequestContent>());
            }

            if (approvalRequests.Count == 0)
            {
                nextMessage = null;
                continue;
            }

            var approvalResponses = new List<AIContent>();

            foreach (var request in approvalRequests)
            {
                var toolName = GetToolName(request.ToolCall);
                var arguments = GetToolArguments(request.ToolCall);
                var approved = _consoleUi.AskForApproval(toolName, arguments);
                approvalResponses.Add(request.CreateResponse(approved));
            }

            nextMessage = new ChatMessage(ChatRole.User, approvalResponses);
            firstRun = false;
        }

        _consoleUi.CompleteStreamingTurn();
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
}
