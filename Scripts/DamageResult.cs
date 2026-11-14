public class DamageResult
{
    public string WeaponId { get; set; }
    public int DiceRoll { get; set; }
    public int Modifier { get; set; }
    public int TotalDamage { get; set; }
    public string DamageType { get; set; }
    public bool IsCritical { get; set; }
}