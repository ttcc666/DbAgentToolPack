using DbAgent.Models;
using Microsoft.Extensions.AI;

namespace DbAgent.Services;

public interface IApprovalPolicy
{
    bool IsAllowed(string toolName, SafetyConfig safety);

    bool RequiresApproval(string toolName);

    string NormalizeToolName(string toolName);

    AITool WrapToolForApprovalIfNeeded(AITool tool);
}
