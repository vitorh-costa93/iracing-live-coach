using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace IracingLiveCoach.Core.Coach;

/// <summary>Plain-text log of the engineer decisions (coach-engineer.log next to coach-settings.json). Append
/// only buffers in memory; a pool thread writes it, so the telemetry thread never touches the disk. When the
/// file exceeds <see cref="MaxBytes"/> it is moved to coach-engineer.log.1 (the previous .1 is replaced).</summary>
public sealed class EngineerLog : IDisposable
{
    public const long MaxBytes = 512 * 1024;
    private const int MaxBufferedLines = 2000;

    private readonly string _path;
    private readonly object _lock = new();
    private readonly object _writeLock = new();
    private readonly List<string> _buffer = new();
    private bool _flushing, _disposed;

    public EngineerLog(string path) => _path = path;

    public static string DefaultPath => Path.Combine(CoachSettings.DefaultDirectory, "coach-engineer.log");

    public void Append(string line)
    {
        lock (_lock)
        {
            if (_disposed) return;
            if (_buffer.Count >= MaxBufferedLines) _buffer.RemoveAt(0);
            _buffer.Add($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {line}");
            if (_flushing) return;
            _flushing = true;
        }
        Task.Run(Flush);
    }

    /// <summary>Writes everything buffered (also called by the background task and Dispose).</summary>
    public void Flush()
    {
        try
        {
            while (true)
            {
                string[] lines;
                lock (_lock)
                {
                    if (_buffer.Count == 0) { _flushing = false; return; }
                    lines = _buffer.ToArray();
                    _buffer.Clear();
                }
                WriteLines(lines);
            }
        }
        catch { lock (_lock) _flushing = false; }
    }

    private void WriteLines(string[] lines)
    {
        try
        {
            lock (_writeLock)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                var info = new FileInfo(_path);
                if (info.Exists && info.Length > MaxBytes)
                    File.Move(_path, _path + ".1", overwrite: true);
                File.AppendAllText(_path, string.Join(Environment.NewLine, lines) + Environment.NewLine, new UTF8Encoding(false));
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    public void Dispose()
    {
        lock (_lock) { if (_disposed) return; _disposed = true; }
        Flush();
    }
}
