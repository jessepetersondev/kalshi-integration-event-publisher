using System.ComponentModel.DataAnnotations;

namespace Kalshi.Integration.Executor.Configuration;

public sealed class ExecutorOptions
{
    public const string SectionName = "Executor";

    [Required]
    public string ServiceName { get; set; } = "Kalshi.Integration.Executor";

    [Required]
    public string ServiceVersion { get; set; } = "v1";

    [Required]
    public string PrimaryQueue { get; set; } = "kalshi.integration.executor";

    [Required]
    public string ResultQueue { get; set; } = "kalshi.integration.executor.results";

    [Required]
    public string DeadLetterQueue { get; set; } = "kalshi.integration.executor.dlq";

    [MinLength(1)]
    public string[] RoutingBindings { get; set; } = ["kalshi.integration.#"];
}
