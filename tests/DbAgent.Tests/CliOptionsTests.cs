using DbAgent.Models;

namespace DbAgent.Tests;

public sealed class CliOptionsTests
{
    [Fact]
    public void Parse_DefaultsToChatWhenNoCommandIsProvided()
    {
        var options = CliOptions.Parse([]);

        Assert.Equal("chat", options.Command);
        Assert.False(options.ShowHelp);
    }

    [Fact]
    public void Parse_ParsesDoctorModelAndConfigArguments()
    {
        var options = CliOptions.Parse(["doctor", "--model", "local-main", "--config", "c:\\temp\\appsettings.json"]);

        Assert.Equal("doctor", options.Command);
        Assert.Equal("local-main", options.ModelName);
        Assert.Equal("c:\\temp\\appsettings.json", options.ConfigPath);
    }

    [Fact]
    public void Parse_ParsesInitForceAndHelp()
    {
        var options = CliOptions.Parse(["init", "--force", "--help"]);

        Assert.Equal("init", options.Command);
        Assert.True(options.Force);
        Assert.True(options.ShowHelp);
    }

    [Fact]
    public void Parse_ThrowsWhenOptionValueIsMissing()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => CliOptions.Parse(["doctor", "--model"]));

        Assert.Equal("参数 --model 需要一个值。", exception.Message);
    }

    [Fact]
    public void Parse_ThrowsWhenUnknownArgumentIsProvided()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => CliOptions.Parse(["models", "--unknown"]));

        Assert.Equal("未知参数：--unknown", exception.Message);
    }
}
