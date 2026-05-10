using DbAgent.Models;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace DbAgent.ConsoleUi;

public interface IConsoleUi
{
    void Clear();

    void MarkupLine(string markup);

    void PrintDivider();

    string? ReadChatInput();

    string PromptRequired(string label);

    string PromptOptional(string label, string defaultValue);

    string PromptSelection(string title, IEnumerable<string> choices);

    void PrintCompactHeader(
        ModelProfile profile,
        SafetyConfig safety,
        int exposedToolCount,
        int totalToolCount,
        Func<ModelProfile, string> displayNameAccessor);

    void PrintCliHelp();

    void PrintHelp();

    void PrintTools(
        IEnumerable<string> allTools,
        IEnumerable<string> exposedTools,
        Func<string, bool> requiresApproval);

    void PrintConfig(
        AppConfig config,
        ModelProfile currentProfile,
        string appConfigPath,
        string databaseConfigPath);

    void PrintModelProfiles(AppConfig config, Func<ModelProfile, string> displayNameAccessor);

    void PrintCurrentModel(
        ModelProfile profile,
        Func<ModelProfile, string> displayNameAccessor,
        Func<ModelProfile, string> apiKeySourceAccessor);

    void PrintCompactTokenUsage(TokenUsageAccumulator turnUsage, TokenUsageAccumulator sessionUsage);

    void PrintDetailedTokenUsage(TokenUsageAccumulator sessionUsage);

    void PrintDoctorReport(IEnumerable<DoctorCheck> checks);

    void PrintSuccessPanel(string message);

    void PrintWarningPanel(string message);

    void PrintErrorPanel(string message);

    void ShowNoToolsWarning();

    void ResetTurnUiState();

    void ShowThinkingStatus();

    void ShowContinueExecutionStatus();

    void HandleStreamingUpdate(AgentResponseUpdate update);

    void CompleteStreamingTurn();

    bool AskForApproval(string toolName, IDictionary<string, object?>? arguments);
}
