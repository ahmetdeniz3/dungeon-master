public class CombatAction
{
    public string ActionType { get; set; }

    public string ActorId { get; set; }
    public string TargetId { get; set; }

    public string WeaponId { get; set; }

    public CombatAttackResult AttackResult { get; set; }
}