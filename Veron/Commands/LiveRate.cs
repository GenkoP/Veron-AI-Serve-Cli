using System;
using System.Collections.Generic;

namespace Veron;

/// <summary>Live tokens/second from (timestamp, n_decoded) samples over a sliding window.</summary>
sealed class LiveRate(TimeSpan window)
{
    readonly Queue<(DateTimeOffset At, int Decoded)> _samples = new();

    int _last;

    public void Reset() => _samples.Clear();

    /// <summary>Add a sample; returns the current rate, or null with fewer than two usable samples.</summary>
    public double? Add(DateTimeOffset at, int decoded)
    {
        // A drop in n_decoded means a new request started.
        if (_samples.Count > 0 && decoded < _last) _samples.Clear();

        _samples.Enqueue((at, decoded));
        _last = decoded;
        while (_samples.Count > 1 && at - _samples.Peek().At > window) _samples.Dequeue();

        if (_samples.Count < 2) return null;
        var first = _samples.Peek();
        double dt = (at - first.At).TotalSeconds;
        return dt <= 0 ? null : (decoded - first.Decoded) / dt;
    }
}
