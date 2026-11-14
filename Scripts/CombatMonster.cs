using System;
using System.Collections.Generic;

public class CombatMonster
{
    public string InstanceId { get; }
    public string DisplayName { get; }
    public MonsterData Data { get; set; }

    public int CurrentHP { get; set; }
    public int MaximumHP { get; set; }

    public WeaponData EquippedWeapon { get; set; }
    public bool NextAttackDisadvantage { get; set; }
    public List<string> ActiveConditions { get; } = new();

    public CombatMonster(MonsterData data, string instanceId = null, string displayName = null)
    {
        Data = data ?? throw new ArgumentNullException(nameof(data));
        InstanceId = string.IsNullOrWhiteSpace(instanceId)
            ? $"{data.Id}_{Guid.NewGuid().ToString("N").Substring(0, 6)}"
            : instanceId;
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? data.Name : displayName;

        MaximumHP = data.Combat?.HitPoints?.Average ?? 1;
        CurrentHP = MaximumHP;
    }

    public void EquipWeapon(WeaponData weapon)
    {
        EquippedWeapon = weapon;
    }

    public void TakeDamage(int damage)
    {
        if (damage < 0)
            damage = 0;

        CurrentHP -= damage;

        if (CurrentHP < 0)
            CurrentHP = 0;
    }

    public bool IsDead()
    {
        return CurrentHP <= 0;
    }
}
