public static class ProficiencyCalculator
{
    public static int GetBonus(int level)
    {
        if (level <= 0)
            return 0;

        return 2 + ((level - 1) / 4);
    }
}