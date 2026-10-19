using System;
using System.Collections.Generic;
using System.Threading;

namespace IracingLiveCoach.Core.Coach;

/// <summary>Single-writer background queue for the coach files (references, history.json). Jobs are
/// coalesced by key (the latest write for a file wins) and run in FIFO order on one dedicated
/// background thread, so the telemetry thread only enqueues. Dispose drains what is pending.</summary>
public sealed class CoachWriteQueue : IDisposable
{
    private readonly object _lock = new();
    private readonly Dictionary<string, Action> _pending = new();
    private readonly Queue<string> _order = new();
    private readonly Thread _thread;
    private bool _busy, _stopping;

    public CoachWriteQueue()
    {
        _thread = new Thread(Run) { IsBackground = true, Name = "coach-writer", Priority = ThreadPriority.BelowNormal };
        _thread.Start();
    }

    /// <summary>Queues a write; a pending write with the same key is replaced. Ignored after Dispose.</summary>
    public void Enqueue(string key, Action write)
    {
        lock (_lock)
        {
            if (_stopping) return;
            if (!_pending.ContainsKey(key)) _order.Enqueue(key);
            _pending[key] = write;
            Monitor.PulseAll(_lock);
        }
    }

    /// <summary>Waits until every queued write has run. Returns false on timeout.</summary>
    public bool Flush(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        lock (_lock)
        {
            while (_order.Count > 0 || _busy)
            {
                var left = deadline - DateTime.UtcNow;
                if (left <= TimeSpan.Zero) return false;
                Monitor.Wait(_lock, left);
            }
            return true;
        }
    }

    private void Run()
    {
        while (true)
        {
            Action job;
            lock (_lock)
            {
                while (_order.Count == 0 && !_stopping) Monitor.Wait(_lock);
                if (_order.Count == 0) return; // stopping and drained
                var key = _order.Dequeue();
                job = _pending[key];
                _pending.Remove(key);
                _busy = true;
            }
            try { job(); } catch { /* persistence is best-effort */ }
            lock (_lock)
            {
                _busy = false;
                Monitor.PulseAll(_lock);
            }
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_stopping) return;
            _stopping = true;
            Monitor.PulseAll(_lock);
        }
        try { _thread.Join(TimeSpan.FromSeconds(5)); } catch { }
    }
}
