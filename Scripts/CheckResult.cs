public class CheckResult
{
    public string CheckType { get; set; }

    public string SkillName { get; set; }

    public int DiceRoll { get; set; }

    public int Modifier { get; set; }

    public int Total { get; set; }

    public bool IsCriticalSuccess { get; set; }

    public bool IsCriticalFailure { get; set; }
}