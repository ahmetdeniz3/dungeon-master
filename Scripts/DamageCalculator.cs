public static class DamageCalculator
{
    public static DamageResult RollWeaponDamage(
        CharacterData character,
        WeaponData weapon,
        int abilityScore,
        bool critical = false)
    {
        int diceRoll = DiceRoller.RollExpression(weapon.DamageDice, critical);
        int modifier = AbilityScoreCalculator.GetModifier(abilityScore);
        int totalDamage = diceRoll + modifier;

        if (totalDamage < 0)
            totalDamage = 0;

        return new DamageResult
        {
            WeaponId = weapon.Id,
            DiceRoll = diceRoll,
            Modifier = modifier,
            TotalDamage = totalDamage,
            DamageType = weapon.DamageType,
            IsCritical = critical
        };
    }
}
