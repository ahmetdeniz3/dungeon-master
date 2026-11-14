using Godot;
using System;

public enum RollAdvantage
{
    Normal,
    Advantage,
    Disadvantage
}

public static class DiceRoller
{
    private static readonly RandomNumberGenerator _rng = new RandomNumberGenerator();

    static DiceRoller()
    {
        _rng.Randomize();
    }

    /// <summary>
    /// Rolls 1d20, optionally with advantage or disadvantage.
    /// </summary>
    public static int RollD20(RollAdvantage advantage = RollAdvantage.Normal)
    {
        int first = Roll(1, 20);
        if (advantage == RollAdvantage.Normal)
            return first;

        int second = Roll(1, 20);
        return advantage == RollAdvantage.Advantage
            ? Math.Max(first, second)
            : Math.Min(first, second);
    }

    /// <summary>
    /// Rolls <paramref name="diceCount"/> dice with <paramref name="diceSides"/> sides.
    /// </summary>
    public static int Roll(int diceCount, int diceSides)
    {
        if (diceCount <= 0 || diceSides <= 0)
            return 0;

        int total = 0;
        for (int i = 0; i < diceCount; i++)
        {
            total += _rng.RandiRange(1, diceSides);
        }

        return total;
    }

    /// <summary>
    /// Parses and rolls dice expressions like "1d8", "2d6+3", or "1d10-1".
    /// </summary>
    public static int RollExpression(string expression, bool critical = false)
    {
        if (!TryParseExpression(expression, out int count, out int sides, out int flatBonus))
            return 0;

        if (critical)
            count *= 2;

        return Roll(count, sides) + flatBonus;
    }

    public static bool TryParseExpression(string expression, out int count, out int sides, out int flatBonus)
    {
        count = 0;
        sides = 0;
        flatBonus = 0;

        if (string.IsNullOrWhiteSpace(expression))
            return false;

        string normalized = expression.Trim().ToLowerInvariant().Replace(" ", "");
        int dIndex = normalized.IndexOf('d');
        if (dIndex < 0)
            return false;

        string countPart = dIndex == 0 ? "1" : normalized.Substring(0, dIndex);
        if (!int.TryParse(countPart, out count) || count <= 0)
            return false;

        string remainder = normalized.Substring(dIndex + 1);
        int bonusIndex = remainder.IndexOfAny(new[] { '+', '-' });

        string sidesPart = bonusIndex >= 0 ? remainder.Substring(0, bonusIndex) : remainder;
        if (!int.TryParse(sidesPart, out sides) || sides <= 0)
            return false;

        if (bonusIndex >= 0)
        {
            string bonusPart = remainder.Substring(bonusIndex);
            if (!int.TryParse(bonusPart, out flatBonus))
                return false;
        }

        return true;
    }
}
