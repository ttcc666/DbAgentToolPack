using DbAgent.Models;

namespace DbAgent.Configuration;

public sealed class DefaultAppConfigContextFactory : IAppConfigContextFactory
{
    private readonly IConfigPathResolver _configPathResolver;
    private readonly IAppConfigStore _appConfigStore;

    public DefaultAppConfigContextFactory(
        IConfigPathResolver configPathResolver,
        IAppConfigStore appConfigStore)
    {
        _configPathResolver = configPathResolver;
        _appConfigStore = appConfigStore;
    }

    public AppConfigContext Create(string? explicitConfigPath)
    {
        var configPath = _configPathResolver.ResolveConfigPath(explicitConfigPath);
        var config = _appConfigStore.Load(configPath);

        if (_appConfigStore.Normalize(config))
        {
            _appConfigStore.Save(config, configPath);
        }

        var configDirectory = Path.GetDirectoryName(configPath)!;
        var databaseConfigPath = PathUtility.ResolvePath(
            configDirectory,
            config.Database.ConfigPath);

        return new AppConfigContext
        {
            Config = config,
            ConfigPath = configPath,
            DatabaseConfigPath = databaseConfigPath
        };
    }
}
