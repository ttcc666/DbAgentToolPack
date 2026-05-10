using DbAgent.Models;

namespace DbAgent.Commands;

public interface ICommandHandler
{
    string CommandName { get; }

    Task<int> ExecuteAsync(CliOptions options, CancellationToken cancellationToken = default);
}
