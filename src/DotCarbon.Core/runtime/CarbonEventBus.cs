using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using System.Diagnostics.CodeAnalysis;

namespace DotCarbon.Core.Runtime;

public readonly record struct CarbonEventName<T>(string Value)
{
    public override string ToString() => Value;
}

public enum CarbonEventTargetKind
{
    All,
    App,
    Window,
}

public sealed record CarbonEventTarget
{
    private CarbonEventTarget(CarbonEventTargetKind kind, string? label)
    {
        Kind = kind;
        Label = label;
    }

    public CarbonEventTargetKind Kind { get; }
    public string? Label { get; }

    public static CarbonEventTarget All { get; } = new(CarbonEventTargetKind.All, null);
    public static CarbonEventTarget App { get; } = new(CarbonEventTargetKind.App, null);

    public static CarbonEventTarget Window(string label)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        return new CarbonEventTarget(CarbonEventTargetKind.Window, label);
    }
}

public sealed record CarbonEvent<T>(
    long Id,
    CarbonEventName<T> Name,
    T Payload,
    string? SourceWindowLabel);

public sealed class CarbonEventSubscription : IDisposable
{
    private CarbonEventBus? _bus;

    internal CarbonEventSubscription(long id, CarbonEventBus bus)
    {
        Id = id;
        _bus = bus;
    }

    public long Id { get; }

    public void Dispose() => Interlocked.Exchange(ref _bus, null)?.Unlisten(Id);
}

public sealed class CarbonEventBus
{
    private readonly object _gate = new();
    private readonly Dictionary<long, Subscription> _subscriptions = [];
    private readonly Dictionary<string, HashSet<long>> _subscriptionsByEvent =
        new(StringComparer.Ordinal);
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly Func<CarbonEventEnvelope, Task> _publish;
    private long _nextId;

    internal CarbonEventBus(
        JsonSerializerOptions jsonOptions,
        Func<CarbonEventEnvelope, Task> publish)
    {
        _jsonOptions = jsonOptions;
        _publish = publish;
    }

    [RequiresUnreferencedCode("Use the JsonTypeInfo overload for trimmed applications.")]
    [RequiresDynamicCode("Use the JsonTypeInfo overload for NativeAOT applications.")]
    public CarbonEventSubscription Listen<T>(
        CarbonEventName<T> name,
        Action<CarbonEvent<T>> handler) =>
        Listen(name, value =>
        {
            handler(value);
            return ValueTask.CompletedTask;
        });

    [RequiresUnreferencedCode("Use the JsonTypeInfo overload for trimmed applications.")]
    [RequiresDynamicCode("Use the JsonTypeInfo overload for NativeAOT applications.")]
    public CarbonEventSubscription Listen<T>(
        CarbonEventName<T> name,
        Func<CarbonEvent<T>, ValueTask> handler) =>
        ListenCore(name.Value, once: false, async envelope =>
        {
            var payload = envelope.Payload is null
                ? default
                : envelope.Payload.Deserialize<T>(_jsonOptions);
            await handler(new CarbonEvent<T>(
                envelope.Id, name, payload!, envelope.SourceWindowLabel));
        });

    public CarbonEventSubscription Listen<T>(
        CarbonEventName<T> name,
        JsonTypeInfo<T> typeInfo,
        Func<CarbonEvent<T>, ValueTask> handler) =>
        ListenCore(name.Value, once: false, async envelope =>
        {
            var payload = envelope.Payload is null
                ? default
                : envelope.Payload.Deserialize(typeInfo);
            await handler(new CarbonEvent<T>(
                envelope.Id, name, payload!, envelope.SourceWindowLabel));
        });

    public CarbonEventSubscription Listen<T>(
        CarbonEventName<T> name,
        JsonTypeInfo<T> typeInfo,
        Action<CarbonEvent<T>> handler) =>
        Listen(name, typeInfo, value =>
        {
            handler(value);
            return ValueTask.CompletedTask;
        });

    [RequiresUnreferencedCode("Use a JsonTypeInfo-based listener for trimmed applications.")]
    [RequiresDynamicCode("Use a JsonTypeInfo-based listener for NativeAOT applications.")]
    public CarbonEventSubscription Once<T>(
        CarbonEventName<T> name,
        Action<CarbonEvent<T>> handler) =>
        Once(name, value =>
        {
            handler(value);
            return ValueTask.CompletedTask;
        });

    [RequiresUnreferencedCode("Use a JsonTypeInfo-based listener for trimmed applications.")]
    [RequiresDynamicCode("Use a JsonTypeInfo-based listener for NativeAOT applications.")]
    public CarbonEventSubscription Once<T>(
        CarbonEventName<T> name,
        Func<CarbonEvent<T>, ValueTask> handler) =>
        ListenCore(name.Value, once: true, async envelope =>
        {
            var payload = envelope.Payload is null
                ? default
                : envelope.Payload.Deserialize<T>(_jsonOptions);
            await handler(new CarbonEvent<T>(
                envelope.Id, name, payload!, envelope.SourceWindowLabel));
        });

    public CarbonEventSubscription Once<T>(
        CarbonEventName<T> name,
        JsonTypeInfo<T> typeInfo,
        Func<CarbonEvent<T>, ValueTask> handler) =>
        ListenCore(name.Value, once: true, async envelope =>
        {
            var payload = envelope.Payload is null
                ? default
                : envelope.Payload.Deserialize(typeInfo);
            await handler(new CarbonEvent<T>(
                envelope.Id, name, payload!, envelope.SourceWindowLabel));
        });

    public CarbonEventSubscription Once<T>(
        CarbonEventName<T> name,
        JsonTypeInfo<T> typeInfo,
        Action<CarbonEvent<T>> handler) =>
        Once(name, typeInfo, value =>
        {
            handler(value);
            return ValueTask.CompletedTask;
        });

    [RequiresUnreferencedCode("Use the JsonTypeInfo overload for trimmed applications.")]
    [RequiresDynamicCode("Use the JsonTypeInfo overload for NativeAOT applications.")]
    public Task EmitAsync<T>(
        CarbonEventName<T> name,
        T payload,
        CarbonEventTarget? target = null) =>
        EmitAsync(name, payload, sourceWindowLabel: null, target);

    public Task EmitAsync<T>(
        CarbonEventName<T> name,
        T payload,
        JsonTypeInfo<T> typeInfo,
        CarbonEventTarget? target = null)
    {
        ValidateEventName(name.Value);
        return _publish(new CarbonEventEnvelope(
            Interlocked.Increment(ref _nextId),
            name.Value,
            JsonSerializer.SerializeToNode(payload, typeInfo),
            SourceWindowLabel: null,
            target ?? CarbonEventTarget.All));
    }

    [RequiresUnreferencedCode("Use the JsonTypeInfo overload for trimmed applications.")]
    [RequiresDynamicCode("Use the JsonTypeInfo overload for NativeAOT applications.")]
    internal Task EmitAsync<T>(
        CarbonEventName<T> name,
        T payload,
        string? sourceWindowLabel,
        CarbonEventTarget? target = null)
    {
        ValidateEventName(name.Value);
        return _publish(new CarbonEventEnvelope(
            Interlocked.Increment(ref _nextId),
            name.Value,
            JsonSerializer.SerializeToNode(payload, _jsonOptions),
            sourceWindowLabel,
            target ?? CarbonEventTarget.All));
    }

    internal Task EmitAsync<T>(
        CarbonEventName<T> name,
        T payload,
        JsonTypeInfo<T> typeInfo,
        string? sourceWindowLabel,
        CarbonEventTarget? target = null)
    {
        ValidateEventName(name.Value);
        return _publish(new CarbonEventEnvelope(
            Interlocked.Increment(ref _nextId),
            name.Value,
            JsonSerializer.SerializeToNode(payload, typeInfo),
            sourceWindowLabel,
            target ?? CarbonEventTarget.All));
    }

    internal Task EmitJsonAsync(
        string name,
        JsonNode? payload,
        string? sourceWindowLabel,
        CarbonEventTarget target)
    {
        ValidateEventName(name);
        return _publish(new CarbonEventEnvelope(
            Interlocked.Increment(ref _nextId),
            name,
            payload?.DeepClone(),
            sourceWindowLabel,
            target));
    }

    public bool Unlisten(long subscriptionId)
    {
        lock (_gate)
        {
            if (!_subscriptions.Remove(subscriptionId, out var subscription)) return false;
            if (_subscriptionsByEvent.TryGetValue(subscription.EventName, out var ids))
            {
                ids.Remove(subscriptionId);
                if (ids.Count == 0) _subscriptionsByEvent.Remove(subscription.EventName);
            }
            return true;
        }
    }

    internal async Task DispatchAsync(CarbonEventEnvelope envelope)
    {
        Subscription[] subscriptions;
        lock (_gate)
        {
            if (!_subscriptionsByEvent.TryGetValue(envelope.Name, out var ids)) return;
            subscriptions = ids
                .Select(id => _subscriptions.GetValueOrDefault(id))
                .Where(subscription => subscription is not null)
                .Cast<Subscription>()
                .ToArray();
            foreach (var subscription in subscriptions.Where(subscription => subscription.Once))
                Unlisten(subscription.Id);
        }

        foreach (var subscription in subscriptions)
        {
            try
            {
                await subscription.Handler(envelope);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(
                    $"[Carbon] Event listener '{envelope.Name}' failed: {ex.Message}");
            }
        }
    }

    private CarbonEventSubscription ListenCore(
        string eventName,
        bool once,
        Func<CarbonEventEnvelope, ValueTask> handler)
    {
        ValidateEventName(eventName);
        ArgumentNullException.ThrowIfNull(handler);
        var id = Interlocked.Increment(ref _nextId);
        var subscription = new Subscription(id, eventName, once, handler);
        lock (_gate)
        {
            _subscriptions[id] = subscription;
            if (!_subscriptionsByEvent.TryGetValue(eventName, out var ids))
                _subscriptionsByEvent[eventName] = ids = [];
            ids.Add(id);
        }
        return new CarbonEventSubscription(id, this);
    }

    private static void ValidateEventName(string name) =>
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

    private sealed record Subscription(
        long Id,
        string EventName,
        bool Once,
        Func<CarbonEventEnvelope, ValueTask> Handler);
}

internal sealed record CarbonEventEnvelope(
    long Id,
    string Name,
    JsonNode? Payload,
    string? SourceWindowLabel,
    CarbonEventTarget Target);
