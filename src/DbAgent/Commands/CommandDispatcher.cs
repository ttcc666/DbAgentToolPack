using DbAgent.ConsoleUi;
using DbAgent.Models;

namespace DbAgent.Commands;

public sealed class CommandDispatcher
{
    private readonly IReadOnlyDictionary<string, ICommandHandler> _handlers;
    private readonly IConsoleUi _consoleUi;

    public CommandDispatcher(IEnumerable<ICommandHandler> handlers, IConsoleUi consoleUi)
    {
        _handlers = handlers.ToDictionary(
            handler => handler.CommandName,
            handler => handler,
            StringComparer.OrdinalIgnoreCase);
        _consoleUi = consoleUi;
    }

    public Task<int> DispatchAsync(CliOptions options, CancellationToken cancellationToken = default)
    {
        if (options.ShowHelp)
        {
            _consoleUi.PrintCliHelp();
            return Task.FromResult(0);
        }

        if (_handlers.TryGetValue(options.Command, out var handler))
        {
            return handler.ExecuteAsync(options, cancellationToken);
        }

        _consoleUi.PrintErrorPanel($"未知命令：{options.Command}");
        _consoleUi.PrintCliHelp();
        return Task.FromResult(2);
    }
}
