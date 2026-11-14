public class InitiativeResult
{
    public string CombatantId { get; set; }
    public string CombatantName { get; set; }
    public bool IsPlayer { get; set; }

    public int DiceRoll { get; set; }
    public int Modifier { get; set; }
    public int Total { get; set; }
}
