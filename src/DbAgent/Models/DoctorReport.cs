namespace DbAgent.Models;

public sealed class DoctorReport
{
    public required IReadOnlyList<DoctorCheck> Checks { get; init; }

    public bool IsSuccess => Checks.All(check => check.IsOk);
}
