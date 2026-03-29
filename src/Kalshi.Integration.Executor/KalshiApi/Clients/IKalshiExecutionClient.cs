using Kalshi.Integration.Application.Events;

namespace Kalshi.Integration.Executor.KalshiApi.Clients;

public interface IKalshiExecutionClient
{
    Task ExecuteAsync(ApplicationEventEnvelope envelope, CancellationToken cancellationToken);
}
