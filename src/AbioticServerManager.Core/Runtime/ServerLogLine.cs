namespace AbioticServerManager.Core.Runtime;

public enum ServerLogSeverity
{
    Info,
    Warning,
    Error,
}

public sealed record ServerLogLine(
    string InstanceId,
    DateTimeOffset Timestamp,
    string Text,
    bool IsError)
{
    /// <summary>
    /// Drives log colouring: red for errors, yellow for warnings, default
    /// otherwise. Error wins over warning when a line trips both.
    /// </summary>
    public ServerLogSeverity Severity =>
        IsError ? ServerLogSeverity.Error
        : ContainsWarningSignal(Text) ? ServerLogSeverity.Warning
        : ServerLogSeverity.Info;

    public static ServerLogLine FromProcessOutput(
        string instanceId,
        DateTimeOffset timestamp,
        string text,
        bool isErrorStream) =>
        new(instanceId, timestamp, text, isErrorStream || ContainsErrorSignal(text));

    public static bool ContainsWarningSignal(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        return ContainsToken(text, "warning") ||
               ContainsToken(text, "warn") ||
               ContainsToken(text, "deprecated");
    }

    public static bool ContainsErrorSignal(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var hasExplicitError = ContainsToken(text, "error") ||
                               ContainsToken(text, "fatal") ||
                               ContainsToken(text, "exception") ||
                               ContainsToken(text, "crash") ||
                               text.Contains("exited unexpectedly", StringComparison.OrdinalIgnoreCase);
        if (hasExplicitError)
        {
            return true;
        }

        var hasFailure = ContainsToken(text, "failed") ||
                         ContainsToken(text, "failure");
        return hasFailure && !ContainsToken(text, "warning");
    }

    private static bool ContainsToken(string text, string token)
    {
        var index = text.IndexOf(token, StringComparison.OrdinalIgnoreCase);
        while (index >= 0)
        {
            var before = index == 0 ? '\0' : text[index - 1];
            var afterIndex = index + token.Length;
            var after = afterIndex >= text.Length ? '\0' : text[afterIndex];

            if (!IsIdentifierCharacter(before) && !IsIdentifierCharacter(after))
            {
                return true;
            }

            index = text.IndexOf(token, index + token.Length, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private static bool IsIdentifierCharacter(char value) =>
        char.IsLetterOrDigit(value) || value is '_';
}

public sealed record ServerStartResult
{
    public required bool Started { get; init; }
    public int? ProcessId { get; init; }
    public string? ErrorMessage { get; init; }

    public static ServerStartResult Ok(int pid) => new() { Started = true, ProcessId = pid };

    public static ServerStartResult Fail(string error) =>
        new() { Started = false, ErrorMessage = error };
}
