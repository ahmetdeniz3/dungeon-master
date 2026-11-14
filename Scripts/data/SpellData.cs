public class SpellData
{
    public string Id { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public int Level { get; set; }
    public string School { get; set; }
    public bool AttackRequired { get; set; }
    public bool SavingThrowRequired { get; set; }
    public string SavingThrowAbility { get; set; }
    public string DamageDice { get; set; }
    public string DamageType { get; set; }
    public string HealingDice { get; set; }
    public bool AutoHit { get; set; }
}
