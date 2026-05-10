using DbAgent.Models;

namespace DbAgent.Services;

public interface IDoctorService
{
    Task<DoctorReport> RunAsync(
        AppConfig config,
        string appConfigPath,
        string? modelName,
        CancellationToken cancellationToken = default);
}
