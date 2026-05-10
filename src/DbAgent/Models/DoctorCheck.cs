namespace DbAgent.Models;

public sealed record DoctorCheck(
    string Name,
    bool IsOk,
    string Detail);
