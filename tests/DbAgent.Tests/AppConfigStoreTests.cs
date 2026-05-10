using DbAgent.Configuration;
using DbAgent.Models;
using DbAgent.Services;
using DbAgent.Tests.TestDoubles;

namespace DbAgent.Tests;

public sealed class AppConfigStoreTests
{
    private readonly JsonAppConfigStore _store = new(
        new DefaultModelProfileService(new FakeAppEnvironment(
            currentDirectory: Path.GetFullPath("current"),
            baseDirectory: Path.GetFullPath("base"),
            userHomeDirectory: Path.GetFullPath("user"))));

    [Fact]
    public void Normalize_MigratesLegacyOpenAiConfiguration()
    {
        var config = new AppConfig
        {
            OpenAI = new LegacyOpenAIConfig
            {
                Endpoint = "https://example.com/v1",
                Model = "gpt-test",
                ApiKey = "key"
            }
        };

        var changed = _store.Normalize(config);

        Assert.True(changed);
        Assert.Null(config.OpenAI);
        Assert.Equal("default", config.Models.Current);
        Assert.Single(config.Models.Profiles);
        Assert.Equal("gpt-test", config.Models.Profiles[0].Model);
    }

    [Fact]
    public void Validate_ThrowsWhenModelListIsEmpty()
    {
        using var tempFile = CreateTempFile("{}");
        var config = new AppConfig();

        var exception = Assert.Throws<InvalidOperationException>(() => _store.Validate(config, tempFile.Path));

        Assert.Equal("至少需要配置一个模型。", exception.Message);
    }

    [Fact]
    public void Validate_ThrowsWhenEndpointIsInvalid()
    {
        using var tempFile = CreateTempFile("{}");
        var config = CreateValidConfig();
        config.Models.Profiles[0].Endpoint = "not-a-uri";

        var exception = Assert.Throws<InvalidOperationException>(() => _store.Validate(config, tempFile.Path));

        Assert.Equal("模型 primary 的 Endpoint 必须是绝对 URL。", exception.Message);
    }

    [Fact]
    public void Validate_ThrowsWhenDatabaseConfigFileIsMissing()
    {
        var config = CreateValidConfig();
        var missingPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");

        var exception = Assert.Throws<FileNotFoundException>(() => _store.Validate(config, missingPath));

        Assert.Equal($"找不到数据库配置文件: {missingPath}", exception.Message);
    }

    private static AppConfig CreateValidConfig()
    {
        return new AppConfig
        {
            Models = new ModelProfilesConfig
            {
                Current = "primary",
                Profiles =
                [
                    new ModelProfile
                    {
                        Name = "primary",
                        Endpoint = "https://example.com/v1",
                        Model = "gpt-test",
                        ApiKey = "secret"
                    }
                ]
            },
            Mcp = new McpConfig
            {
                Command = "DatabaseMcpServer"
            }
        };
    }

    private static TemporaryFile CreateTempFile(string content)
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, content);
        return new TemporaryFile(path);
    }

    private sealed class TemporaryFile : IDisposable
    {
        public TemporaryFile(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public void Dispose()
        {
            if (File.Exists(Path))
            {
                File.Delete(Path);
            }
        }
    }
}
