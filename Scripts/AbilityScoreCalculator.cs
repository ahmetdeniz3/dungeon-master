using System;

public static class AbilityScoreCalculator
{
    public static int GetModifier(int abilityScore)
    {
        return (int)Math.Floor((abilityScore - 10) / 2.0);
    }
}