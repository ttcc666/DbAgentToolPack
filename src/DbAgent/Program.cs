using System.Text;
using DbAgent.Commands;
using DbAgent.ConsoleUi;
using DbAgent.Extensions;
using DbAgent.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DbAgent;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        using var host = CreateHost(args).Build();
        var consoleUi = host.Services.GetRequiredService<IConsoleUi>();

        CliOptions options;

        try
        {
            options = CliOptions.Parse(args);
        }
        catch (Exception ex)
        {
            consoleUi.PrintErrorPanel(ex.Message);
            consoleUi.PrintCliHelp();
            return 2;
        }

        return await host.Services
            .GetRequiredService<CommandDispatcher>()
            .DispatchAsync(options)
            .ConfigureAwait(false);
    }

    private static HostApplicationBuilder CreateHost(string[] args)
    {
        var builder = global::Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder(args);

        builder.Logging.ClearProviders();
        builder.Logging.AddSimpleConsole(options =>
        {
            options.SingleLine = true;
            options.TimestampFormat = "HH:mm:ss ";
        });
        builder.Logging.SetMinimumLevel(LogLevel.Warning);

        builder.Services.AddDbAgent();

        return builder;
    }
}
