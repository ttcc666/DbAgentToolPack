using DbAgent.Configuration;
using DbAgent.Tests.TestDoubles;

namespace DbAgent.Tests;

public sealed class ConfigPathResolverTests
{
    [Fact]
    public void ResolveConfigPath_PrefersExplicitPath()
    {
        var explicitPath = Path.GetFullPath("explicit/appsettings.json");
        var resolver = CreateResolver(existingFiles: [explicitPath], variables: new Dictionary<string, string?>
        {
            ["DBAGENT_CONFIG"] = "env/appsettings.json"
        });

        var resolved = resolver.ResolveConfigPath(explicitPath);

        Assert.Equal(explicitPath, resolved);
    }

    [Fact]
    public void ResolveConfigPath_FallsBackThroughConfiguredPrecedence()
    {
        var envPath = Path.GetFullPath("env/appsettings.json");
        var currentPath = Path.GetFullPath("current/appsettings.json");
        var userPath = Path.GetFullPath("user/.dbagent/appsettings.json");
        var basePath = Path.GetFullPath("base/appsettings.json");

        var envResolver = CreateResolver(existingFiles: [envPath], variables: new Dictionary<string, string?>
        {
            ["DBAGENT_CONFIG"] = envPath
        });
        Assert.Equal(envPath, envResolver.ResolveConfigPath(null));

        var currentResolver = CreateResolver(existingFiles: [currentPath]);
        Assert.Equal(currentPath, currentResolver.ResolveConfigPath(null));

        var userResolver = CreateResolver(existingFiles: [userPath]);
        Assert.Equal(userPath, userResolver.ResolveConfigPath(null));

        var baseResolver = CreateResolver(existingFiles: [basePath]);
        Assert.Equal(basePath, baseResolver.ResolveConfigPath(null));
    }

    [Fact]
    public void ResolveConfigPath_ThrowsWhenNoCandidateExists()
    {
        var resolver = CreateResolver();

        var exception = Assert.Throws<FileNotFoundException>(() => resolver.ResolveConfigPath(null));

        Assert.Equal("找不到 appsettings.json。可运行 dbagent init 生成默认配置，或使用 --config 指定配置文件。", exception.Message);
    }

    private static DefaultConfigPathResolver CreateResolver(
        IEnumerable<string>? existingFiles = null,
        IDictionary<string, string?>? variables = null)
    {
        var environment = new FakeAppEnvironment(
            currentDirectory: Path.GetFullPath("current"),
            baseDirectory: Path.GetFullPath("base"),
            userHomeDirectory: Path.GetFullPath("user"),
            existingFiles: existingFiles,
            variables: variables);

        return new DefaultConfigPathResolver(environment);
    }
}
