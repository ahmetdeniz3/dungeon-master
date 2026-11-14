public class AttackResult
{
    public string AttackType { get; set; }

    public string WeaponId { get; set; }

    public int DiceRoll { get; set; }

    public int Modifier { get; set; }

    public int Total { get; set; }

    public int TargetArmorClass { get; set; }

    public bool IsHit { get; set; }

    public bool IsCriticalHit { get; set; }

    public bool IsCriticalMiss { get; set; }
}