namespace PopFs.Localization;

internal sealed record FontGlyphPlan(int[] Codes, IReadOnlyList<(int Old, int New)> Replacements)
{
    public static FontGlyphPlan Create(IEnumerable<int> originalCodes,
        IEnumerable<int> requiredCodes)
    {
        var original = originalCodes.ToHashSet();
        var required = requiredCodes.ToHashSet();
        var missing = required.Except(original).Order().ToArray();

        // Keep the descriptor's glyph count unchanged. The game fails to draw
        // text when this count grows, even if the descriptor remains parseable.
        var candidates = original.Where(c => c is >= 'a' and <= 'z').Order()
            .Concat(original.Where(c => c > 127 && char.IsLower((char)c)).Order())
            .Where(c => !required.Contains(c))
            .Distinct().Take(missing.Length).ToArray();
        if (candidates.Length != missing.Length)
            throw new InvalidDataException($"Need {missing.Length} free glyph slots, found {candidates.Length}.");

        var codes = original.Except(candidates).Concat(missing).Order().ToArray();
        if (codes.Length != original.Count || !required.IsSubsetOf(codes))
            throw new InvalidDataException("Font glyph replacement changed the descriptor count or lost a required character.");
        return new FontGlyphPlan(codes, candidates.Zip(missing).ToArray());
    }
}
