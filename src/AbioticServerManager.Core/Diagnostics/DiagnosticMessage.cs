namespace AbioticServerManager.Core.Diagnostics;

public enum DiagnosticSeverity
{
    Info,
    Success,
    Warning,
    Error,
}

/// <summary>
/// A single human-readable finding from validation or a health check. Always carries a
/// stable <see cref="Code"/> so the UI and tests can react without string-matching prose.
/// </summary>
public sealed record DiagnosticMessage
{
    public required DiagnosticSeverity Severity { get; init; }
    public required string Code { get; init; }
    public required string Title { get; init; }
    public required string Message { get; init; }
    public string? SuggestedFix { get; init; }

    public static DiagnosticMessage Error(string code, string title, string message, string? fix = null) =>
        new() { Severity = DiagnosticSeverity.Error, Code = code, Title = title, Message = message, SuggestedFix = fix };

    public static DiagnosticMessage Warning(string code, string title, string message, string? fix = null) =>
        new() { Severity = DiagnosticSeverity.Warning, Code = code, Title = title, Message = message, SuggestedFix = fix };

    public static DiagnosticMessage Info(string code, string title, string message) =>
        new() { Severity = DiagnosticSeverity.Info, Code = code, Title = title, Message = message };

    public static DiagnosticMessage Success(string code, string title, string message) =>
        new() { Severity = DiagnosticSeverity.Success, Code = code, Title = title, Message = message };
}
