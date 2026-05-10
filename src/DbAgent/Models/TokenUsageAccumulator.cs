namespace DbAgent.Models;

public sealed class TokenUsageAccumulator
{
    public bool HasUsage { get; set; }
    public long InputTokens { get; set; }
    public long OutputTokens { get; set; }
    public long TotalTokens { get; set; }
    public long CachedInputTokens { get; set; }
    public long ReasoningTokens { get; set; }

    public void Add(TokenUsageAccumulator other)
    {
        HasUsage |= other.HasUsage;
        InputTokens += other.InputTokens;
        OutputTokens += other.OutputTokens;
        TotalTokens += other.TotalTokens;
        CachedInputTokens += other.CachedInputTokens;
        ReasoningTokens += other.ReasoningTokens;
    }

    public void Reset()
    {
        HasUsage = false;
        InputTokens = 0;
        OutputTokens = 0;
        TotalTokens = 0;
        CachedInputTokens = 0;
        ReasoningTokens = 0;
    }
}
