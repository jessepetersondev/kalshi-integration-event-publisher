using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Kalshi.Integration.Application.Abstractions;
using Kalshi.Integration.Application.Trading;
using Kalshi.Integration.Contracts.Kalshi;
using Kalshi.Integration.Contracts.Orders;
using Kalshi.Integration.Contracts.TradeIntents;
using Kalshi.Integration.Domain.Common;
using Kalshi.Integration.Domain.TradeIntents;
using Kalshi.Integration.Infrastructure.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Kalshi.Integration.Infrastructure.Integrations.Kalshi;

/// <summary>
/// Exposes a Kalshi-compatible bridge on top of publisher-owned order tracking.
/// </summary>
public sealed class KalshiBridgeService
{
    private static readonly HashSet<string> TerminalStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "filled",
        "rejected",
        "canceled",
        "cancelled",
        "settled",
    };

    private readonly IKalshiApiClient _kalshiApiClient;
    private readonly IOrderRepository _orderRepository;
    private readonly ITradeIntentRepository _tradeIntentRepository;
    private readonly OrderSubmissionService _orderSubmissionService;
    private readonly PublisherCommandOutboxDispatcher _outboxDispatcher;
    private readonly TradingService _tradingService;
    private readonly TradingQueryService _tradingQueryService;
    private readonly KalshiApiOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="KalshiBridgeService"/> class.
    /// </summary>
    /// <param name="kalshiApiClient">The direct Kalshi API client.</param>
    /// <param name="orderRepository">The repository used to find related cancel commands.</param>
    /// <param name="tradeIntentRepository">The repository used to find matching cancel trade-intents.</param>
    /// <param name="applicationEventPublisher">The publisher used to emit order commands from the outbox dispatcher.</param>
    /// <param name="tradingService">The publisher trading workflow service.</param>
    /// <param name="tradingQueryService">The publisher trading query service.</param>
    /// <param name="options">The Kalshi bridge configuration.</param>
    public KalshiBridgeService(
        IKalshiApiClient kalshiApiClient,
        IOrderRepository orderRepository,
        ITradeIntentRepository tradeIntentRepository,
        IApplicationEventPublisher applicationEventPublisher,
        TradingService tradingService,
        TradingQueryService tradingQueryService,
        IOptions<KalshiApiOptions> options)
        : this(
            kalshiApiClient,
            orderRepository,
            tradeIntentRepository,
            new OrderSubmissionService(
                tradeIntentRepository,
                orderRepository as IOrderCommandSubmissionStore ?? throw new InvalidOperationException("Order repository must support durable command submission."),
                orderRepository),
            new PublisherCommandOutboxDispatcher(
                orderRepository as IPublisherCommandOutboxStore ?? throw new InvalidOperationException("Order repository must support durable outbox dispatch."),
                applicationEventPublisher,
                NullOperationalIssueStore.Instance,
                Options.Create(new RabbitMqOptions()),
                NullLogger<PublisherCommandOutboxDispatcher>.Instance),
            tradingService,
            tradingQueryService,
            options)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="KalshiBridgeService"/> class.
    /// </summary>
    /// <param name="kalshiApiClient">The direct Kalshi API client.</param>
    /// <param name="orderRepository">The repository used to find related cancel commands.</param>
    /// <param name="tradeIntentRepository">The repository used to find matching cancel trade-intents.</param>
    /// <param name="orderSubmissionService">The service that persists orders and queues durable order commands.</param>
    /// <param name="outboxDispatcher">The dispatcher used for best-effort immediate command publication.</param>
    /// <param name="tradingService">The publisher trading workflow service.</param>
    /// <param name="tradingQueryService">The publisher trading query service.</param>
    /// <param name="options">The Kalshi bridge configuration.</param>
    [ActivatorUtilitiesConstructor]
    public KalshiBridgeService(
        IKalshiApiClient kalshiApiClient,
        IOrderRepository orderRepository,
        ITradeIntentRepository tradeIntentRepository,
        OrderSubmissionService orderSubmissionService,
        PublisherCommandOutboxDispatcher outboxDispatcher,
        TradingService tradingService,
        TradingQueryService tradingQueryService,
        IOptions<KalshiApiOptions> options)
    {
        _kalshiApiClient = kalshiApiClient;
        _orderRepository = orderRepository;
        _tradeIntentRepository = tradeIntentRepository;
        _orderSubmissionService = orderSubmissionService;
        _outboxDispatcher = outboxDispatcher;
        _tradingService = tradingService;
        _tradingQueryService = tradingQueryService;
        _options = options.Value;
    }

    /// <summary>
    /// Returns the raw Kalshi series payload.
    /// </summary>
    public Task<JsonNode> GetSeriesAsync(string? category, IReadOnlyList<string> tags, CancellationToken cancellationToken = default)
        => _kalshiApiClient.GetSeriesAsync(category, tags, cancellationToken);

    /// <summary>
    /// Returns the raw Kalshi markets payload.
    /// </summary>
    public Task<JsonNode> GetMarketsAsync(string? status, int limit, string? seriesTicker, string? cursor, CancellationToken cancellationToken = default)
        => _kalshiApiClient.GetMarketsAsync(status, limit, seriesTicker, cursor, cancellationToken);

    /// <summary>
    /// Returns the raw Kalshi market payload for a single ticker.
    /// </summary>
    public Task<JsonNode> GetMarketAsync(string ticker, CancellationToken cancellationToken = default)
        => _kalshiApiClient.GetMarketAsync(ticker, cancellationToken);

    /// <summary>
    /// Returns the raw Kalshi balance payload for the configured subaccount.
    /// </summary>
    public Task<JsonNode> GetBalanceAsync(CancellationToken cancellationToken = default)
        => _kalshiApiClient.GetBalanceAsync(_options.Subaccount, cancellationToken);

    /// <summary>
    /// Returns the raw Kalshi positions payload for the configured subaccount.
    /// </summary>
    public Task<JsonNode> GetPositionsAsync(CancellationToken cancellationToken = default)
        => _kalshiApiClient.GetPositionsAsync(_options.Subaccount, cancellationToken);

    /// <summary>
    /// Places an order through the publisher workflow and emits the canonical order-created event.
    /// </summary>
    public async Task<JsonNode> PlaceOrderAsync(SubmitKalshiOrderRequest request, CancellationToken cancellationToken = default)
    {
        var tradeIntent = await _tradingService.CreateTradeIntentAsync(BuildTradeIntentRequest(request), cancellationToken);
        var order = await _orderSubmissionService.SubmitOrderAsync(
            new CreateOrderRequest(tradeIntent.Id),
            tradeIntent.CorrelationId,
            tradeIntent.CorrelationId,
            BuildCommandAttributes(request),
            cancellationToken);

        if (order.CommandEventId.HasValue)
        {
            try
            {
                await _outboxDispatcher.DispatchAsync(order.CommandEventId.Value, cancellationToken);
            }
            catch
            {
                // The durable outbox remains the source of truth.
            }
        }

        order = await WaitForOrderActivityAsync(order.Id, order.UpdatedAt, cancellationToken);
        return BuildBridgeOrderEnvelope(order);
    }

    /// <summary>
    /// Returns the latest bridge order payload for the supplied publisher order id.
    /// </summary>
    public async Task<JsonNode> GetOrderAsync(Guid publisherOrderId, CancellationToken cancellationToken = default)
    {
        var order = await GetRequiredOrderAsync(publisherOrderId, cancellationToken);
        var latestCancelOrder = await GetLatestCancelOrderAsync(publisherOrderId, cancellationToken);
        return BuildBridgeOrderEnvelope(
            order,
            bridgeStatusOverride: ResolveCancelBridgeStatusOverride(latestCancelOrder),
            updatedAtOverride: latestCancelOrder?.UpdatedAt);
    }

    /// <summary>
    /// Cancels the supplied publisher order by publishing a cancel command through the workflow.
    /// </summary>
    public async Task<JsonNode> CancelOrderAsync(Guid publisherOrderId, CancellationToken cancellationToken = default)
    {
        var order = await GetRequiredOrderAsync(publisherOrderId, cancellationToken);
        if (IsTerminalStatus(order.Status))
        {
            return BuildBridgeOrderEnvelope(order);
        }

        var cancelOrder = await GetLatestCancelOrderAsync(publisherOrderId, cancellationToken);
        if (cancelOrder is null)
        {
            var cancelTradeIntent = await _tradingService.CreateTradeIntentAsync(BuildCancelTradeIntentRequest(order), cancellationToken);
            cancelOrder = await _orderSubmissionService.SubmitOrderAsync(
                new CreateOrderRequest(cancelTradeIntent.Id),
                cancelTradeIntent.CorrelationId,
                cancelTradeIntent.CorrelationId,
                additionalAttributes: null,
                cancellationToken);

            if (cancelOrder.CommandEventId.HasValue)
            {
                try
                {
                    await _outboxDispatcher.DispatchAsync(cancelOrder.CommandEventId.Value, cancellationToken);
                }
                catch
                {
                    // The durable outbox remains the source of truth.
                }
            }

            cancelOrder = await WaitForOrderActivityAsync(cancelOrder.Id, cancelOrder.UpdatedAt, cancellationToken);
        }

        return BuildBridgeOrderEnvelope(
            order,
            bridgeStatusOverride: ResolveCancelBridgeStatusOverride(cancelOrder),
            updatedAtOverride: cancelOrder?.UpdatedAt);
    }

    private static CreateTradeIntentRequest BuildTradeIntentRequest(SubmitKalshiOrderRequest request)
    {
        var normalizedSide = NormalizeSide(request.Side);
        var actionType = NormalizeActionType(request.Action);
        var limitPrice = ResolveLimitPrice(request, normalizedSide);
        var correlationId = !string.IsNullOrWhiteSpace(request.CorrelationId)
            ? request.CorrelationId.Trim()
            : (!string.IsNullOrWhiteSpace(request.ClientOrderId) ? request.ClientOrderId.Trim() : Guid.NewGuid().ToString("N"));

        return new CreateTradeIntentRequest(
            request.Ticker,
            normalizedSide,
            request.Count,
            limitPrice,
            string.IsNullOrWhiteSpace(request.StrategyName) ? "kalshi-btc-quant" : request.StrategyName.Trim(),
            correlationId,
            actionType,
            string.IsNullOrWhiteSpace(request.OriginService) ? "kalshi-btc-quant" : request.OriginService.Trim(),
            string.IsNullOrWhiteSpace(request.DecisionReason) ? $"{actionType} via kalshi bridge" : request.DecisionReason.Trim(),
            string.IsNullOrWhiteSpace(request.CommandSchemaVersion) ? "kalshi-btc-quant.bridge.v1" : request.CommandSchemaVersion.Trim(),
            actionType == "exit" ? request.Ticker : null,
            actionType == "exit" ? normalizedSide : null,
            null,
            null,
            null);
    }

    private async Task<OrderResponse> GetRequiredOrderAsync(Guid publisherOrderId, CancellationToken cancellationToken)
    {
        return await _tradingQueryService.GetOrderAsync(publisherOrderId, cancellationToken)
            ?? throw new KeyNotFoundException($"Order '{publisherOrderId}' was not found.");
    }

    private static Dictionary<string, string?>? BuildCommandAttributes(SubmitKalshiOrderRequest? bridgeRequest)
    {
        if (bridgeRequest is null)
        {
            return null;
        }

        return new Dictionary<string, string?>
        {
            ["timeInForce"] = NormalizeTimeInForce(bridgeRequest.TimeInForce),
            ["postOnly"] = bridgeRequest.PostOnly ? "true" : "false",
            ["cancelOrderOnPause"] = bridgeRequest.CancelOrderOnPause ? "true" : "false",
        };
    }

    private sealed class NullOperationalIssueStore : IOperationalIssueStore
    {
        public static NullOperationalIssueStore Instance { get; } = new();

        public Task AddAsync(Application.Operations.OperationalIssue issue, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<IReadOnlyList<Application.Operations.OperationalIssue>> GetRecentAsync(string? category = null, int hours = 24, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<Application.Operations.OperationalIssue>>(Array.Empty<Application.Operations.OperationalIssue>());
    }

    private async Task<OrderResponse> WaitForOrderActivityAsync(Guid orderId, DateTimeOffset baselineUpdatedAt, CancellationToken cancellationToken)
    {
        var timeoutAt = DateTimeOffset.UtcNow.AddSeconds(2);
        OrderResponse current = await GetRequiredOrderAsync(orderId, cancellationToken);

        while (DateTimeOffset.UtcNow < timeoutAt)
        {
            if (current.UpdatedAt > baselineUpdatedAt
                || !string.Equals(current.Status, "pending", StringComparison.OrdinalIgnoreCase)
                || !string.IsNullOrWhiteSpace(current.ExternalOrderId))
            {
                return current;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
            current = await GetRequiredOrderAsync(orderId, cancellationToken);
        }

        return current;
    }

    private async Task<OrderResponse?> GetLatestCancelOrderAsync(Guid targetPublisherOrderId, CancellationToken cancellationToken)
    {
        var cancelTradeIntent = await _tradeIntentRepository.FindMatchingCancelTradeIntentAsync(
            targetPublisherOrderId,
            targetClientOrderId: null,
            targetExternalOrderId: null,
            cancellationToken);

        if (cancelTradeIntent is null)
        {
            return null;
        }

        var cancelOrder = await _orderRepository.GetLatestOrderByTradeIntentIdAsync(cancelTradeIntent.Id, cancellationToken);

        return cancelOrder is null
            ? null
            : await _tradingQueryService.GetOrderAsync(cancelOrder.Id, cancellationToken);
    }

    private static CreateTradeIntentRequest BuildCancelTradeIntentRequest(OrderResponse order)
    {
        return new CreateTradeIntentRequest(
            order.Ticker,
            null,
            null,
            null,
            order.StrategyName,
            $"{order.CorrelationId}:cancel:{Guid.NewGuid():N}",
            "cancel",
            order.OriginService,
            $"Cancel bridge order {order.Id}",
            order.CommandSchemaVersion,
            null,
            null,
            order.Id,
            order.ClientOrderId ?? order.CorrelationId,
            order.ExternalOrderId);
    }

    private static bool IsTerminalStatus(string status)
        => TerminalStatuses.Contains(status);

    private static string? NormalizeTimeInForce(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        return string.Equals(normalized, "good_til_cancelled", StringComparison.OrdinalIgnoreCase)
            ? "good_till_canceled"
            : normalized;
    }

    private static string? ResolveCancelBridgeStatusOverride(OrderResponse? cancelOrder)
    {
        if (cancelOrder is null)
        {
            return null;
        }

        if (string.Equals(cancelOrder.Status, "rejected", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (string.Equals(cancelOrder.Status, "canceled", StringComparison.OrdinalIgnoreCase)
            || NormalizeToken(cancelOrder.PublishStatus) == "publishconfirmed")
        {
            return "canceled";
        }

        return null;
    }

    private static JsonObject BuildBridgeOrderEnvelope(OrderResponse order, string? bridgeStatusOverride = null, DateTimeOffset? updatedAtOverride = null)
    {
        var orderNode = new JsonObject();
        var totalContracts = order.Quantity ?? 0;
        var filledContracts = order.FilledQuantity;
        var remainingContracts = Math.Max(totalContracts - filledContracts, 0);
        var side = NormalizeSide(order.Side);

        orderNode["order_id"] = order.Id.ToString();
        orderNode["publisher_order_id"] = order.Id.ToString();
        orderNode["trade_intent_id"] = order.TradeIntentId.ToString();
        orderNode["external_order_id"] = order.ExternalOrderId;
        orderNode["client_order_id"] = order.ClientOrderId ?? order.CorrelationId;
        orderNode["ticker"] = order.Ticker;
        orderNode["side"] = side;
        orderNode["action"] = ResolveBridgeAction(order.ActionType);
        orderNode["status"] = bridgeStatusOverride ?? ResolveBridgeStatus(order.Status, remainingContracts);
        orderNode["type"] = "limit";
        orderNode["initial_count_fp"] = totalContracts;
        orderNode["fill_count_fp"] = filledContracts;
        orderNode["remaining_count_fp"] = remainingContracts;
        orderNode["created_time"] = order.CreatedAt.ToString("O", CultureInfo.InvariantCulture);
        orderNode["last_update_time"] = (updatedAtOverride ?? order.UpdatedAt).ToString("O", CultureInfo.InvariantCulture);

        if (string.Equals(side, "yes", StringComparison.Ordinal))
        {
            orderNode["yes_price_dollars"] = order.LimitPrice;
            orderNode["no_price_dollars"] = null;
        }
        else
        {
            orderNode["yes_price_dollars"] = null;
            orderNode["no_price_dollars"] = order.LimitPrice;
        }

        return new JsonObject
        {
            ["order"] = orderNode,
        };
    }

    private static string NormalizeSide(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainException("Side is required.");
        }

        var normalized = value.Trim().ToLowerInvariant();
        if (normalized is "yes" or "no")
        {
            return normalized;
        }

        throw new DomainException("Side must be either 'yes' or 'no'.");
    }

    private static string NormalizeActionType(string value)
    {
        var normalized = value.Trim().ToLowerInvariant();
        return normalized switch
        {
            "buy" => "entry",
            "sell" => "exit",
            _ => throw new DomainException("Action must be either 'buy' or 'sell'."),
        };
    }

    private static decimal ResolveLimitPrice(SubmitKalshiOrderRequest request, string normalizedSide)
    {
        var price = string.Equals(normalizedSide, "yes", StringComparison.Ordinal)
            ? request.YesPriceDollars
            : request.NoPriceDollars;

        if (!price.HasValue || price.Value <= 0m || price.Value > 1m)
        {
            throw new DomainException("A side-specific limit price between 0 and 1 is required.");
        }

        return decimal.Round(price.Value, 4, MidpointRounding.AwayFromZero);
    }

    private static string ResolveBridgeAction(string actionType)
    {
        return actionType.Trim().ToLowerInvariant() switch
        {
            "exit" => "sell",
            "cancel" => "cancel",
            _ => "buy",
        };
    }

    private static string ResolveBridgeStatus(string publisherStatus, int remainingContracts)
    {
        var normalizedPublisherStatus = NormalizeToken(publisherStatus);
        return normalizedPublisherStatus switch
        {
            "accepted" => "open",
            "partiallyfilled" when remainingContracts > 0 => "resting",
            "filled" => "filled",
            "cancelled" => "canceled",
            _ => publisherStatus.Trim().ToLowerInvariant(),
        };
    }

    private static string NormalizeToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        foreach (var character in value.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(character);
            }
        }

        return builder.ToString();
    }
}
