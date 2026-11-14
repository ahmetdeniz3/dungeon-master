public static class SavingThrowCalculator
{
    public static CheckResult RollSavingThrow(
        CharacterData character,
        string abilityName,
        int abilityScore,
        RollAdvantage advantage = RollAdvantage.Normal)
    {
        string normalized = abilityName?.Trim().ToLowerInvariant() ?? "";
        SavingThrowData savingThrow = character.SavingThrows.TryGetValue(normalized, out SavingThrowData found)
            ? found
            : new SavingThrowData { Proficient = false };

        int modifier = AbilityScoreCalculator.GetModifier(abilityScore);
        int proficiencyBonus = ProficiencyCalculator.GetBonus(character.Level);

        if (savingThrow.Proficient)
            modifier += proficiencyBonus;

        int roll = DiceRoller.RollD20(advantage);

        return new CheckResult
        {
            CheckType = "SavingThrow",
            SkillName = normalized,
            DiceRoll = roll,
            Modifier = modifier,
            Total = roll + modifier,
            IsCriticalSuccess = roll == 20,
            IsCriticalFailure = roll == 1
        };
    }
}
