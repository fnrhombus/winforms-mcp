namespace Rhombus.WinFormsMcp.Rendering;

/// <summary>
/// Sentinel value indicating an expression could not be evaluated
/// (e.g., resource references, typeof(), unsupported syntax).
/// Used by renderers to skip property assignments without throwing.
/// </summary>
internal sealed class EvaluationSkipped {
    public static readonly EvaluationSkipped Instance = new();
    private EvaluationSkipped() { }
}