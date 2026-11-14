public static class SkillCheckCalculator
{
    public static CheckResult RollSkillCheck(
        CharacterData character,
        string skillName,
        int abilityScore,
        RollAdvantage advantage = RollAdvantage.Normal)
    {
        string normalized = skillName?.Trim().ToLowerInvariant() ?? "";
        SkillData skill = character.Skills.TryGetValue(normalized, out SkillData found)
            ? found
            : new SkillData { Proficient = false, Expertise = false };

        int modifier = SkillCalculator.GetSkillModifier(
            abilityScore,
            character.Level,
            skill
        );

        int roll = DiceRoller.RollD20(advantage);

        return new CheckResult
        {
            CheckType = "SkillCheck",
            SkillName = normalized,
            DiceRoll = roll,
            Modifier = modifier,
            Total = roll + modifier,
            IsCriticalSuccess = roll == 20,
            IsCriticalFailure = roll == 1
        };
    }
}
