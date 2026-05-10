using DbAgent.Configuration;
using DbAgent.ConsoleUi;
using DbAgent.Models;
using DbAgent.Services;

namespace DbAgent.Commands;

public sealed class ModelsCommandHandler : ICommandHandler
{
    private readonly IConsoleUi _consoleUi;
    private readonly IAppConfigContextFactory _contextFactory;
    private readonly IModelProfileService _modelProfileService;

    public ModelsCommandHandler(
        IConsoleUi consoleUi,
        IAppConfigContextFactory contextFactory,
        IModelProfileService modelProfileService)
    {
        _consoleUi = consoleUi;
        _contextFactory = contextFactory;
        _modelProfileService = modelProfileService;
    }

    public string CommandName => "models";

    public Task<int> ExecuteAsync(CliOptions options, CancellationToken cancellationToken = default)
    {
        AppConfigContext context;

        try
        {
            context = _contextFactory.Create(options.ConfigPath);
        }
        catch (FileNotFoundException ex)
        {
            _consoleUi.PrintErrorPanel(ex.Message);
            _consoleUi.MarkupLine("[grey]提示：首次使用可运行 [white]dbagent init[/] 生成配置。[/]");
            return Task.FromResult(2);
        }
        catch (Exception ex)
        {
            _consoleUi.PrintErrorPanel($"读取配置失败：{ex.Message}");
            return Task.FromResult(2);
        }

        _consoleUi.PrintModelProfiles(context.Config, _modelProfileService.GetProfileDisplayName);
        return Task.FromResult(0);
    }
}
