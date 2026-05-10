using DbAgent.Models;
using DbAgent.Services;
using DbAgent.Tests.TestDoubles;

namespace DbAgent.Tests;

public sealed class ModelProfileServiceTests
{
    [Fact]
    public void ResolveApiKey_PrefersEnvironmentVariableWhenAvailable()
    {
        var service = CreateService(new Dictionary<string, string?>
        {
            ["DBAGENT_API_KEY"] = "from-env"
        });

        var apiKey = service.ResolveApiKey(new ModelProfile
        {
            Name = "primary",
            Endpoint = "https://example.com/v1",
            Model = "gpt-test",
            ApiKey = "from-config",
            ApiKeyEnvironmentVariable = "DBAGENT_API_KEY"
        });

        Assert.Equal("from-env", apiKey);
    }

    [Fact]
    public void ResolveApiKey_FallsBackToConfigWhenEnvironmentVariableIsMissing()
    {
        var service = CreateService();

        var apiKey = service.ResolveApiKey(new ModelProfile
        {
            Name = "primary",
            Endpoint = "https://example.com/v1",
            Model = "gpt-test",
            ApiKey = "from-config",
            ApiKeyEnvironmentVariable = "DBAGENT_API_KEY"
        });

        Assert.Equal("from-config", apiKey);
    }

    [Fact]
    public void GetStartupProfile_UsesNamedModelWhenProvided()
    {
        var service = CreateService();
        var config = CreateConfig();

        var profile = service.GetStartupProfile(config, "secondary");

        Assert.Equal("secondary", profile.Name);
    }

    [Fact]
    public void RemoveProfile_ReassignsCurrentWhenRemovingActiveProfile()
    {
        var service = CreateService();
        var config = CreateConfig();

        var result = service.RemoveProfile(config, "primary", "primary");

        Assert.True(result.RemovedCurrentProfile);
        Assert.NotNull(result.NextCurrentProfile);
        Assert.Equal("secondary", result.NextCurrentProfile!.Name);
        Assert.Equal("secondary", config.Models.Current);
        Assert.Single(config.Models.Profiles);
    }

    [Fact]
    public void RemoveProfile_ThrowsWhenTryingToRemoveLastProfile()
    {
        var service = CreateService();
        var config = new AppConfig
        {
            Models = new ModelProfilesConfig
            {
                Current = "only",
                Profiles =
                [
                    new ModelProfile
                    {
                        Name = "only",
                        Endpoint = "https://example.com/v1",
                        Model = "gpt-test",
                        ApiKey = "secret"
                    }
                ]
            }
        };

        var exception = Assert.Throws<InvalidOperationException>(() =>
            service.RemoveProfile(config, "only", "only"));

        Assert.Equal("至少需要保留一个模型配置。", exception.Message);
    }

    private static DefaultModelProfileService CreateService(
        IDictionary<string, string?>? variables = null)
    {
        return new DefaultModelProfileService(new FakeAppEnvironment(
            currentDirectory: Path.GetFullPath("current"),
            baseDirectory: Path.GetFullPath("base"),
            userHomeDirectory: Path.GetFullPath("user"),
            variables: variables));
    }

    private static AppConfig CreateConfig()
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
                    },
                    new ModelProfile
                    {
                        Name = "secondary",
                        Endpoint = "https://example.com/v1",
                        Model = "gpt-test-2",
                        ApiKey = "secret-2"
                    }
                ]
            }
        };
    }
}
