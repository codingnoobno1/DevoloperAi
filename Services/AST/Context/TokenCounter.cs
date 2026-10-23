namespace Syncro.Desktop.Services.AST.Context
{
    /// <summary>Counts tokens for LLM budgeting. Swappable for an exact BPE backend later.</summary>
    public interface ITokenCounter
    {
        int Count(string text);
    }

    /// <summary>
    /// Dependency-free heuristic: ~4 chars/token, well-calibrated for code and JSON. This is the floor
    /// everything is budgeted against; an exact tokenizer can refine it without changing callers.
    /// </summary>
    public sealed class HeuristicTokenCounter : ITokenCounter
    {
        public int Count(string text) =>
            string.IsNullOrEmpty(text) ? 0 : (text.Length + 3) / 4;
    }
}
