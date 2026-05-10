using DbAgent.Models;

namespace DbAgent.Configuration;

public interface IAppConfigContextFactory
{
    AppConfigContext Create(string? explicitConfigPath);
}
