namespace AbioticServerManager.App.ViewModels;

// Heterogeneous chip rendering: the Logs notification strip blends three
// data sources (recommended actions, integrity findings, and individual
// diagnostic messages) into one WrapPanel. Each chip type carries a
// distinct CLR type so XAML DataTemplates (keyed by DataType) can pick
// the right template without a converter or selector.

/// <summary>Summary chip representing the world's recommended actions list.</summary>
public sealed record RecommendedActionsChip(ServerInstanceViewModel World);

/// <summary>Summary chip representing the world's integrity findings list.</summary>
public sealed record IntegrityFindingsChip(ServerInstanceViewModel World);
