using DbAgent.Models;
using DbAgent.Services;

namespace DbAgent.Tests;

public sealed class ApprovalPolicyTests
{
    private readonly DefaultApprovalPolicy _policy = new();

    [Fact]
    public void IsAllowed_AllowsReadOnlyToolsInReadOnlyMode()
    {
        var allowed = _policy.IsAllowed("sql_query", new SafetyConfig());

        Assert.True(allowed);
    }

    [Fact]
    public void IsAllowed_BlocksWriteToolsWhenWriteModeIsDisabled()
    {
        var allowed = _policy.IsAllowed("execute_command", new SafetyConfig());

        Assert.False(allowed);
    }

    [Fact]
    public void IsAllowed_AllowsWriteToolsWhenWriteModeIsEnabled()
    {
        var allowed = _policy.IsAllowed("execute_command", new SafetyConfig
        {
            AllowWriteTools = true
        });

        Assert.True(allowed);
    }

    [Fact]
    public void RequiresApproval_ReturnsTrueForDangerousTools()
    {
        Assert.True(_policy.RequiresApproval("drop_table"));
        Assert.True(_policy.RequiresApproval("execute_command"));
        Assert.False(_policy.RequiresApproval("sql_query"));
    }
}
