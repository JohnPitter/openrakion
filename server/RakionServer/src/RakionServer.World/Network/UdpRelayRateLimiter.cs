using System;
using System.Collections.Concurrent;

namespace RakionServer.World.Network;

public sealed class UdpRelayRateLimiter
{
    private readonly int _tokensPerSecond;
    private readonly int _burst;
    private double _tokens;
    private long _lastMs;

    public UdpRelayRateLimiter(int tokensPerSecond, int burst, long nowMs)
    {
        _tokensPerSecond = Math.Max(1, tokensPerSecond);
        _burst = Math.Max(_tokensPerSecond, burst);
        _tokens = _burst;
        _lastMs = nowMs;
    }

    public bool TryConsume(long nowMs)
    {
        lock (this)
        {
            long elapsed = Math.Max(0, nowMs - _lastMs);
            _tokens = Math.Min(_burst, _tokens + elapsed * _tokensPerSecond / 1000d);
            _lastMs = Math.Max(_lastMs, nowMs);
            if (_tokens < 1) return false;
            _tokens--;
            return true;
        }
    }
}

public sealed class UdpRelayLimiterRegistry
{
    private readonly int _tokensPerSecond;
    private readonly int _burst;
    private readonly ConcurrentDictionary<ushort, Entry> _entries = new();

    public UdpRelayLimiterRegistry(int tokensPerSecond, int burst)
    {
        _tokensPerSecond = tokensPerSecond;
        _burst = burst;
    }

    public bool TryConsume(ushort slot, uint sessionKey, long nowMs)
    {
        Entry entry = _entries.AddOrUpdate(
            slot,
            _ => NewEntry(sessionKey, nowMs),
            (_, current) => current.SessionKey == sessionKey
                ? current
                : NewEntry(sessionKey, nowMs));
        return entry.Limiter.TryConsume(nowMs);
    }

    private Entry NewEntry(uint sessionKey, long nowMs) =>
        new(sessionKey, new UdpRelayRateLimiter(_tokensPerSecond, _burst, nowMs));

    private sealed record Entry(uint SessionKey, UdpRelayRateLimiter Limiter);
}
