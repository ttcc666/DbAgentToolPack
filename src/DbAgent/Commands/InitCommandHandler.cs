using DbAgent.Configuration;
using DbAgent.ConsoleUi;
using DbAgent.Models;

namespace DbAgent.Commands;

public sealed class InitCommandHandler : ICommandHandler
{
    private readonly IConsoleUi _consoleUi;
    private readonly IConfigPathResolver _configPathResolver;
    private readonly IAppConfigStore _appConfigStore;

    public InitCommandHandler(
        IConsoleUi consoleUi,
        IConfigPathResolver configPathResolver,
        IAppConfigStore appConfigStore)
    {
        _consoleUi = consoleUi;
        _configPathResolver = configPathResolver;
        _appConfigStore = appConfigStore;
    }

    public string CommandName => "init";

    public Task<int> ExecuteAsync(CliOptions options, CancellationToken cancellationToken = default)
    {
        var configPath = !string.IsNullOrWhiteSpace(options.ConfigPath)
            ? Path.GetFullPath(options.ConfigPath)
            : _configPathResolver.GetDefaultConfigPath();

        var configDirectory = Path.GetDirectoryName(configPath)!;
        var databasePath = Path.Combine(configDirectory, "databases.json");

        Directory.CreateDirectory(configDirectory);

        if (!options.Force && (File.Exists(configPath) || File.Exists(databasePath)))
        {
            _consoleUi.PrintWarningPanel(
                $"配置文件已存在。若要覆盖，请使用 --force。\n{configPath}");
            return Task.FromResult(1);
        }

        File.WriteAllText(configPath, _appConfigStore.DefaultAppSettingsJson);
        File.WriteAllText(databasePath, _appConfigStore.DefaultDatabasesJson);

        _consoleUi.PrintSuccessPanel(
            $"已生成配置：\n{configPath}\n{databasePath}");
        _consoleUi.MarkupLine("[grey]下一步：编辑配置后运行 [white]dbagent[/]。[/]");

        return Task.FromResult(0);
    }
}
