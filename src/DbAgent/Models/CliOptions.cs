namespace DbAgent.Models;

public sealed class CliOptions
{
    public string Command { get; private set; } = "chat";
    public string? ConfigPath { get; private set; }
    public string? ModelName { get; private set; }
    public bool Force { get; private set; }
    public bool ShowHelp { get; private set; }
    public string? Query { get; private set; }
    public bool AutoApprove { get; private set; }

    public static CliOptions Parse(string[] args)
    {
        var result = new CliOptions();
        var queue = new Queue<string>(args);

        if (queue.Count > 0 && !queue.Peek().StartsWith("--", StringComparison.Ordinal))
        {
            result.Command = queue.Dequeue().Trim().ToLowerInvariant();
        }

        if (queue.Count > 0 && !queue.Peek().StartsWith("--", StringComparison.Ordinal))
        {
            result.Query = queue.Dequeue();
        }

        while (queue.Count > 0)
        {
            var arg = queue.Dequeue();

            switch (arg)
            {
                case "-h":
                case "--help":
                    result.ShowHelp = true;
                    break;

                case "--config":
                    result.ConfigPath = RequireValue(arg, queue);
                    break;

                case "--model":
                    result.ModelName = RequireValue(arg, queue);
                    break;

                case "--force":
                    result.Force = true;
                    break;

                case "--yes":
                case "-y":
                    result.AutoApprove = true;
                    break;

                default:
                    throw new InvalidOperationException($"未知参数：{arg}");
            }
        }

        return result;
    }

    private static string RequireValue(string option, Queue<string> queue)
    {
        if (queue.Count == 0)
        {
            throw new InvalidOperationException($"参数 {option} 需要一个值。");
        }

        return queue.Dequeue();
    }
}
