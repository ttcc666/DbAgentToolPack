using DbAgent.Models;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;

namespace DbAgent.Services;

public interface IAgentFactory
{
    StdioClientTransport CreateTransport(AppConfig config, string databaseConfigPath);

    IChatClient CreateChatClient(ModelProfile profile);

    AIAgent CreateAgent(ModelProfile profile, SafetyConfig safety, AITool[] tools);
}
