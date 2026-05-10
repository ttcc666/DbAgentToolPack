namespace DbAgent.Models;

public sealed class ModelRemovalResult
{
    public required ModelProfile RemovedProfile { get; init; }
    public required bool RemovedCurrentProfile { get; init; }
    public ModelProfile? NextCurrentProfile { get; init; }
}
