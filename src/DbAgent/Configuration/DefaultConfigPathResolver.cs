using DbAgent.Environment;

namespace DbAgent.Configuration;

public sealed class DefaultConfigPathResolver : IConfigPathResolver
{
    private readonly IAppEnvironment _appEnvironment;

    public DefaultConfigPathResolver(IAppEnvironment appEnvironment)
    {
        _appEnvironment = appEnvironment;
    }

    public string ResolveConfigPath(string? explicitConfigPath)
    {
        var candidates = new List<string>();

        if (!string.IsNullOrWhiteSpace(explicitConfigPath))
        {
            candidates.Add(Path.GetFullPath(explicitConfigPath));
        }

        var envConfig = _appEnvironment.GetEnvironmentVariable("DBAGENT_CONFIG");
        if (!string.IsNullOrWhiteSpace(envConfig))
        {
            candidates.Add(Path.GetFullPath(envConfig));
        }

        candidates.Add(Path.Combine(_appEnvironment.CurrentDirectory, "appsettings.json"));
        candidates.Add(GetDefaultConfigPath());
        candidates.Add(Path.Combine(_appEnvironment.BaseDirectory, "appsettings.json"));

        var found = candidates.FirstOrDefault(_appEnvironment.FileExists);

        return found ?? throw new FileNotFoundException(
            "找不到 appsettings.json。可运行 dbagent init 生成默认配置，或使用 --config 指定配置文件。");
    }

    public string GetDefaultConfigPath()
    {
        return Path.Combine(_appEnvironment.UserHomeDirectory, ".dbagent", "appsettings.json");
    }
}
