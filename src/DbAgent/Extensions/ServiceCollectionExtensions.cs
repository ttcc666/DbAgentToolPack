using DbAgent.Commands;
using DbAgent.Configuration;
using DbAgent.ConsoleUi;
using DbAgent.Environment;
using DbAgent.Services;
using Microsoft.Extensions.DependencyInjection;

namespace DbAgent.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDbAgent(this IServiceCollection services)
    {
        services.AddSingleton<IAppEnvironment, SystemAppEnvironment>();
        services.AddSingleton<IConfigPathResolver, DefaultConfigPathResolver>();
        services.AddSingleton<IModelProfileService, DefaultModelProfileService>();
        services.AddSingleton<IAppConfigStore, JsonAppConfigStore>();
        services.AddSingleton<IAppConfigContextFactory, DefaultAppConfigContextFactory>();
        services.AddSingleton<IApprovalPolicy, DefaultApprovalPolicy>();
        services.AddSingleton<IAgentFactory, DefaultAgentFactory>();
        services.AddSingleton<IChatSessionRunner, ChatSessionRunner>();
        services.AddSingleton<IDoctorService, DoctorService>();
        services.AddSingleton<IConsoleUi, SpectreConsoleUi>();

        services.AddSingleton<ICommandHandler, InitCommandHandler>();
        services.AddSingleton<ICommandHandler, DoctorCommandHandler>();
        services.AddSingleton<ICommandHandler, ModelsCommandHandler>();
        services.AddSingleton<ICommandHandler, ChatCommandHandler>();
        services.AddSingleton<CommandDispatcher>();

        return services;
    }
}
