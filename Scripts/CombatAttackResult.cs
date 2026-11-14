public class CombatAttackResult
{
    public AttackResult Attack { get; set; }
    public DamageResult Damage { get; set; }

    public bool Hit { get; set; }
    public bool Critical { get; set; }
    public int DamageDealt { get; set; }
    public int TargetCurrentHP { get; set; }
    public bool TargetDead { get; set; }
}