using System.Text;
using AbioticServerManager.Core.Runtime;
using Microsoft.Extensions.Logging;

namespace AbioticServerManager.Infrastructure.Runtime;

/// <summary>
/// Follows a world's <c>AbioticFactor/Saved/Logs/AbioticFactor.log</c> while the
/// server runs. The captured process stdout only carries Display-level lines, so
/// the rich <c>LogNet:</c> connection/login lines (SteamID, platform, address,
/// disconnect) only exist in this file. New lines are pushed to a callback as
/// <see cref="ServerLogLine"/>s for the roster/health pipeline.
/// </summary>
public sealed class AbioticServerLogTail : IDisposable
{
    private readonly string _instanceId;
    private readonly string _logPath;
    private readonly Action<IReadOnlyList<ServerLogLine>> _onLines;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _cts = new();
    private Task? _loop;
    private long _position;

    private AbioticServerLogTail(
        string instanceId,
        string logPath,
        Action<IReadOnlyList<ServerLogLine>> onLines,
        ILogger logger)
    {
        _instanceId = instanceId;
        _logPath = logPath;
        _onLines = onLines;
        _logger = logger;
    }

    public static string ResolveLogPath(string installPath) =>
        string.IsNullOrWhiteSpace(installPath)
            ? ""
            : Path.Combine(
                installPath, "AbioticFactor", "Saved", "Logs", "AbioticFactor.log");

    /// <summary>
    /// Starts following the log. New lines are delivered as a BATCH per read
    /// tick (not line-by-line) so the consumer can refresh the UI once per
    /// tick instead of once per line - a join burst of LogNet lines would
    /// otherwise saturate the UI thread.
    /// </summary>
    public static AbioticServerLogTail Start(
        string instanceId,
        string installPath,
        Action<IReadOnlyList<ServerLogLine>> onLines,
        ILogger logger)
    {
        var tail = new AbioticServerLogTail(
            instanceId, ResolveLogPath(installPath), onLines, logger);
        tail._loop = Task.Run(() => tail.RunAsync(tail._cts.Token));
        return tail;
    }

    private async Task RunAsync(CancellationToken ct)
    {
        // Start at end-of-file: a fresh run rotates the old log to a backup and
        // creates a new AbioticFactor.log, so we only ever want NEW lines.
        var primed = false;
        var carry = new StringBuilder();

        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (File.Exists(_logPath))
                {
                    using var fs = new FileStream(
                        _logPath,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.ReadWrite | FileShare.Delete);

                    if (!primed)
                    {
                        _position = fs.Length;
                        primed = true;
                    }
                    else if (fs.Length < _position)
                    {
                        // Rotated/truncated: the server restarted into a new file.
                        _position = 0;
                        carry.Clear();
                    }

                    if (fs.Length > _position)
                    {
                        fs.Seek(_position, SeekOrigin.Begin);
                        using var reader = new StreamReader(fs, Encoding.UTF8);
                        var chunk = await reader.ReadToEndAsync(ct).ConfigureAwait(false);
                        _position = fs.Length;
                        EmitLines(carry, chunk);
                    }
                }

                await Task.Delay(750, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Transient (rotation/sharing); retry on the next tick.
                try
                {
                    await Task.Delay(1000, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }

    private void EmitLines(StringBuilder carry, string chunk)
    {
        carry.Append(chunk);
        var text = carry.ToString();
        var start = 0;
        var batch = new List<ServerLogLine>();

        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != '\n')
            {
                continue;
            }

            var line = text[start..i].TrimEnd('\r');
            start = i + 1;
            if (line.Length == 0)
            {
                continue;
            }

            batch.Add(new ServerLogLine(_instanceId, DateTimeOffset.Now, line, IsError: false));
        }

        carry.Clear();
        if (start < text.Length)
        {
            carry.Append(text[start..]); // keep the partial trailing line
        }

        if (batch.Count == 0)
        {
            return;
        }

        try
        {
            // One dispatch per read tick - the consumer refreshes the UI once.
            _onLines(batch);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Roster/health tail handler threw");
        }
    }

    public void Dispose()
    {
        try
        {
            _cts.Cancel();
            _loop?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (Exception ex) when (ex is AggregateException or OperationCanceledException)
        {
            // Best effort on shutdown.
        }
        finally
        {
            _cts.Dispose();
        }
    }
}
