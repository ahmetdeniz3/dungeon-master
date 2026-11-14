public static class InitiativeCalculator
{
    public static InitiativeResult RollCharacterInitiative(CharacterData character)
    {
        int roll = DiceRoller.RollD20();
        int total = roll + character.InitiativeBonus;

        return new InitiativeResult
        {
            CombatantId = character.Id,
            CombatantName = character.Name,
            IsPlayer = true,
            DiceRoll = roll,
            Modifier = character.InitiativeBonus,
            Total = total
        };
    }

    public static InitiativeResult RollMonsterInitiative(CombatMonster monster)
    {
        int roll = DiceRoller.RollD20();
        int modifier = monster.Data.Combat?.InitiativeModifier ?? 0;

        return new InitiativeResult
        {
            CombatantId = monster.InstanceId,
            CombatantName = monster.DisplayName,
            IsPlayer = false,
            DiceRoll = roll,
            Modifier = modifier,
            Total = roll + modifier
        };
    }

    public static InitiativeResult RollMonsterInitiative(MonsterData monster)
    {
        int roll = DiceRoller.RollD20();
        int modifier = monster.Combat?.InitiativeModifier ?? 0;

        return new InitiativeResult
        {
            CombatantId = monster.Id,
            CombatantName = monster.Name,
            IsPlayer = false,
            DiceRoll = roll,
            Modifier = modifier,
            Total = roll + modifier
        };
    }
}
