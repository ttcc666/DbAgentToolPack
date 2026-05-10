using DbAgent.Models;
using Microsoft.Extensions.AI;

namespace DbAgent.Services;

public sealed class DefaultApprovalPolicy : IApprovalPolicy
{
    private static readonly HashSet<string> ReadOnlyTools = new(StringComparer.OrdinalIgnoreCase)
    {
        "testconnection",
        "testconnectionbyname",
        "getdatabaseconfig",
        "validateconfiguration",
        "listdatabases",
        "switchdatabase",
        "getcurrentdatabase",
        "healthcheck",
        "testconnectionwithretry",
        "sqlquery",
        "sqlquerysingle",
        "getdatasetall",
        "getscalar",
        "sqlquerywithinparameter",
        "getdatabaselist",
        "getviewinfolist",
        "gettableinfolist",
        "getcolumninfosbytablename",
        "gettableschema",
        "getisidentities",
        "getprimaries",
        "getindexlist",
        "getproclist",
        "getfunclist",
        "gettriggernames",
        "isanytable",
        "isanycolumn",
        "isanyconstraint",
        "isanytableremark",
        "generatedatabasedocumentation"
    };

    private static readonly HashSet<string> WriteTools = new(StringComparer.OrdinalIgnoreCase)
    {
        "executecommand",
        "callstoredprocedure",
        "callstoredprocedurewithoutput",
        "executecommandwithgo",
        "batchexecutecommands"
    };

    private static readonly HashSet<string> SchemaWriteTools = new(StringComparer.OrdinalIgnoreCase)
    {
        "droptable",
        "truncatetable",
        "backuptable",
        "renametable",
        "addcolumn",
        "updatecolumn",
        "dropcolumn",
        "renamecolumn",
        "addprimarykey",
        "dropconstraint",
        "createindex",
        "adddefaultvalue",
        "addtableremark",
        "deletetableremark",
        "addcolumnremark",
        "deletecolumnremark",
        "dropview",
        "dropfunc",
        "dropproc"
    };

    public bool IsAllowed(string toolName, SafetyConfig safety)
    {
        var normalized = NormalizeToolName(toolName);

        if (ReadOnlyTools.Contains(normalized))
        {
            return true;
        }

        if (safety.AllowWriteTools && WriteTools.Contains(normalized))
        {
            return true;
        }

        if (safety.AllowSchemaWriteTools && SchemaWriteTools.Contains(normalized))
        {
            return true;
        }

        return false;
    }

    public bool RequiresApproval(string toolName)
    {
        var normalized = NormalizeToolName(toolName);
        return WriteTools.Contains(normalized) || SchemaWriteTools.Contains(normalized);
    }

    public string NormalizeToolName(string toolName)
    {
        return new string(toolName
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray());
    }

    public AITool WrapToolForApprovalIfNeeded(AITool tool)
    {
        if (tool is AIFunction function && RequiresApproval(function.Name))
        {
            return new ApprovalRequiredAIFunction(function);
        }

        return tool;
    }
}
