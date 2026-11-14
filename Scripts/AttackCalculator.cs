public static class AttackCalculator
{
    public static AttackResult RollAttack(
        CharacterData character,
        string attackType,
        WeaponData weapon,
        int targetArmorClass,
        RollAdvantage advantage = RollAdvantage.Normal)
    {
        int abilityScore = GetWeaponAbilityScore(character, weapon);
        int abilityModifier = AbilityScoreCalculator.GetModifier(abilityScore);
        int proficiencyBonus = ProficiencyCalculator.GetBonus(character.Level);
        int modifier = abilityModifier + proficiencyBonus;

        int roll = DiceRoller.RollD20(advantage);
        int total = roll + modifier;
        bool isCriticalHit = roll == 20;
        bool isCriticalMiss = roll == 1;

        return new AttackResult
        {
            AttackType = attackType,
            WeaponId = weapon.Id,
            DiceRoll = roll,
            Modifier = modifier,
            Total = total,
            TargetArmorClass = targetArmorClass,
            IsCriticalHit = isCriticalHit,
            IsCriticalMiss = isCriticalMiss,
            IsHit = isCriticalHit || (!isCriticalMiss && total >= targetArmorClass)
        };
    }

    public static int GetWeaponAbilityScore(CharacterData character, WeaponData weapon)
    {
        if (weapon.Finesse)
        {
            int strength = character.AbilityScores.Strength;
            int dexterity = character.AbilityScores.Dexterity;
            return strength > dexterity ? strength : dexterity;
        }

        if (string.Equals(weapon.Ability, "dexterity", System.StringComparison.OrdinalIgnoreCase))
            return character.AbilityScores.Dexterity;

        return character.AbilityScores.Strength;
    }
}
