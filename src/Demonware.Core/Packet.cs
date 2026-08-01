namespace Demonware.Core;

/// <summary>Lightweight bag attached to a TCP connection event.</summary>
public sealed class Packet
{
    private readonly Dictionary<string, object?> _bag = new(StringComparer.Ordinal);

    public object? this[string key]
    {
        get => _bag.TryGetValue(key, out var v) ? v : null;
        set => _bag[key] = value;
    }

    public T? Get<T>(string key)
    {
        if (!_bag.TryGetValue(key, out var v) || v is null) return default;
        if (v is T t) return t;
        try { return (T)Convert.ChangeType(v, typeof(T)); }
        catch { return default; }
    }

    public string ConnectionId => Get<string>("cid") ?? "";
}
