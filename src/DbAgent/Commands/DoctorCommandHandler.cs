using DbAgent.Configuration;
using DbAgent.ConsoleUi;
using DbAgent.Models;
using DbAgent.Services;

namespace DbAgent.Commands;

public sealed class DoctorCommandHandler : ICommandHandler
{
    private readonly IConsoleUi _consoleUi;
    private readonly IAppConfigContextFactory _contextFactory;
    private readonly IDoctorService _doctorService;

    public DoctorCommandHandler(
        IConsoleUi consoleUi,
        IAppConfigContextFactory contextFactory,
        IDoctorService doctorService)
    {
        _consoleUi = consoleUi;
        _contextFactory = contextFactory;
        _doctorService = doctorService;
    }

    public string CommandName => "doctor";

    public async Task<int> ExecuteAsync(CliOptions options, CancellationToken cancellationToken = default)
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
            return 2;
        }
        catch (Exception ex)
        {
            _consoleUi.PrintErrorPanel($"读取配置失败：{ex.Message}");
            return 2;
        }

        var report = await _doctorService
            .RunAsync(
                context.Config,
                context.ConfigPath,
                options.ModelName,
                cancellationToken)
            .ConfigureAwait(false);

        _consoleUi.PrintDoctorReport(report.Checks);
        return report.IsSuccess ? 0 : 1;
    }
}
