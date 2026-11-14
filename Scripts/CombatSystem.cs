using System;
using System.Collections.Generic;

public class SpellCastResult
{
    public SpellData Spell { get; set; }
    public bool Hit { get; set; }
    public int AttackRoll { get; set; }
    public int AttackTotal { get; set; }
    public int DamageDealt { get; set; }
    public int HealingDone { get; set; }
    public bool TargetDead { get; set; }
    public string Summary { get; set; }
}

public static class CombatSystem
{
    public static RollAdvantage CombineAdvantage(bool hasAdvantage, bool hasDisadvantage)
    {
        if (hasAdvantage == hasDisadvantage)
            return RollAdvantage.Normal;

        return hasAdvantage ? RollAdvantage.Advantage : RollAdvantage.Disadvantage;
    }

    public static bool HasAttackDisadvantage(IEnumerable<string> conditions)
    {
        if (conditions == null || DataManager.Instance == null)
            return false;

        foreach (string condId in conditions)
        {
            ConditionData cond = DataManager.Instance.GetConditionData(condId);
            if (cond != null && cond.AttackDisadvantage)
                return true;
        }

        return false;
    }

    public static bool HasIncomingAttackAdvantage(IEnumerable<string> conditions)
    {
        if (conditions == null || DataManager.Instance == null)
            return false;

        foreach (string condId in conditions)
        {
            ConditionData cond = DataManager.Instance.GetConditionData(condId);
            if (cond != null && cond.IncomingAttackAdvantage)
                return true;
        }

        return false;
    }

    public static bool CannotAct(IEnumerable<string> conditions)
    {
        if (conditions == null || DataManager.Instance == null)
            return false;

        foreach (string condId in conditions)
        {
            ConditionData cond = DataManager.Instance.GetConditionData(condId);
            if (cond != null && cond.CannotAct)
                return true;
        }

        return false;
    }

    public static CombatAttackResult PerformWeaponAttack(
        CharacterData character,
        WeaponData weapon,
        CombatMonster target,
        RollAdvantage advantage = RollAdvantage.Normal)
    {
        string attackType = weapon.Category != null && weapon.Category.Contains("ranged", StringComparison.OrdinalIgnoreCase)
            ? "Ranged"
            : "Melee";

        AttackResult attack = AttackCalculator.RollAttack(
            character,
            attackType,
            weapon,
            target.Data.Combat.ArmorClass,
            advantage
        );

        var result = new CombatAttackResult
        {
            Attack = attack,
            Hit = attack.IsHit,
            Critical = attack.IsCriticalHit
        };

        if (!attack.IsHit)
        {
            result.Damage = null;
            result.DamageDealt = 0;
            result.TargetCurrentHP = target.CurrentHP;
            result.TargetDead = target.IsDead();
            return result;
        }

        int damageAbilityScore = AttackCalculator.GetWeaponAbilityScore(character, weapon);
        DamageResult damage = DamageCalculator.RollWeaponDamage(
            character,
            weapon,
            damageAbilityScore,
            attack.IsCriticalHit
        );

        result.Damage = damage;
        result.DamageDealt = damage.TotalDamage;

        target.TakeDamage(damage.TotalDamage);

        if (string.Equals(weapon.Mastery, "Sap", StringComparison.OrdinalIgnoreCase))
            target.NextAttackDisadvantage = true;

        result.TargetCurrentHP = target.CurrentHP;
        result.TargetDead = target.IsDead();
        return result;
    }

    public static CombatAttackResult PerformMonsterAttack(
        CombatMonster monster,
        MonsterAttackData attackData,
        CharacterData target,
        RollAdvantage advantage = RollAdvantage.Normal)
    {
        if (monster == null || attackData == null || target == null)
            return null;

        int roll = DiceRoller.RollD20(advantage);
        int total = roll + attackData.AttackBonus;
        bool criticalHit = roll == 20;
        bool criticalMiss = roll == 1;
        bool isHit = criticalHit || (!criticalMiss && total >= target.ArmorClass);

        var attack = new AttackResult
        {
            AttackType = "Melee",
            WeaponId = attackData.WeaponId,
            DiceRoll = roll,
            Modifier = attackData.AttackBonus,
            Total = total,
            TargetArmorClass = target.ArmorClass,
            IsHit = isHit,
            IsCriticalHit = criticalHit,
            IsCriticalMiss = criticalMiss
        };

        var result = new CombatAttackResult
        {
            Attack = attack,
            Hit = isHit,
            Critical = criticalHit
        };

        if (!isHit)
        {
            result.Damage = null;
            result.DamageDealt = 0;
            result.TargetCurrentHP = target.CurrentHP;
            result.TargetDead = target.CurrentHP <= 0;
            return result;
        }

        int diceRoll = DiceRoller.RollExpression(attackData.DamageDice, criticalHit);
        int totalDamage = diceRoll + attackData.DamageBonus;
        if (totalDamage < 0)
            totalDamage = 0;

        result.Damage = new DamageResult
        {
            WeaponId = attackData.WeaponId,
            DiceRoll = diceRoll,
            Modifier = attackData.DamageBonus,
            TotalDamage = totalDamage,
            DamageType = attackData.DamageType,
            IsCritical = criticalHit
        };
        result.DamageDealt = totalDamage;

        target.CurrentHP -= totalDamage;
        if (target.CurrentHP < 0)
            target.CurrentHP = 0;

        result.TargetCurrentHP = target.CurrentHP;
        result.TargetDead = target.CurrentHP <= 0;
        return result;
    }

    public static SpellCastResult CastSpell(
        CharacterData caster,
        SpellData spell,
        CombatMonster target,
        RollAdvantage advantage = RollAdvantage.Normal)
    {
        if (caster == null || spell == null)
            return null;

        if (spell.Level > 0)
        {
            if (!caster.SpellSlotsCurrent.TryGetValue(spell.Level, out int slots) || slots <= 0)
                return null;

            caster.SpellSlotsCurrent[spell.Level] = slots - 1;
        }

        int abilityScore = SkillCalculator.GetAbilityScoreByName(caster.AbilityScores, caster.SpellcastingAbility);
        int spellMod = AbilityScoreCalculator.GetModifier(abilityScore);
        int prof = ProficiencyCalculator.GetBonus(caster.Level);

        if (!string.IsNullOrEmpty(spell.HealingDice))
        {
            int heal = Math.Max(1, DiceRoller.RollExpression(spell.HealingDice) + spellMod);
            int before = caster.CurrentHP;
            caster.CurrentHP = Math.Min(caster.MaximumHP, caster.CurrentHP + heal);
            int actual = caster.CurrentHP - before;

            return new SpellCastResult
            {
                Spell = spell,
                Hit = true,
                HealingDone = actual,
                Summary = $"✨ {caster.Name}, {spell.Name} büyüsü yaptı -> +{actual} HP iyileşti! ({caster.CurrentHP}/{caster.MaximumHP})"
            };
        }

        if (target == null)
            return null;

        if (spell.AutoHit)
        {
            int dmg = Math.Max(1, DiceRoller.RollExpression(spell.DamageDice));
            target.TakeDamage(dmg);
            return new SpellCastResult
            {
                Spell = spell,
                Hit = true,
                DamageDealt = dmg,
                TargetDead = target.IsDead(),
                Summary = $"✨ {caster.Name}, {spell.Name} ile {target.DisplayName}'e doğrudan {dmg} {spell.DamageType} hasar verdi!"
            };
        }

        int roll = DiceRoller.RollD20(advantage);
        bool crit = roll == 20;
        bool miss = roll == 1;
        int total = roll + spellMod + prof;
        bool hit = crit || (!miss && total >= target.Data.Combat.ArmorClass);

        if (!hit)
        {
            return new SpellCastResult
            {
                Spell = spell,
                Hit = false,
                AttackRoll = roll,
                AttackTotal = total,
                Summary = $"✨ {caster.Name}, {spell.Name} ile {target.DisplayName}'e saldırdı ama ıskaladı! (Atış: {roll} + {spellMod + prof} = {total})"
            };
        }

        int spellDamage = Math.Max(1, DiceRoller.RollExpression(spell.DamageDice, crit));
        target.TakeDamage(spellDamage);

        return new SpellCastResult
        {
            Spell = spell,
            Hit = true,
            AttackRoll = roll,
            AttackTotal = total,
            DamageDealt = spellDamage,
            TargetDead = target.IsDead(),
            Summary = $"✨ {caster.Name}, {spell.Name} ile {target.DisplayName}'e {(crit ? "[KRİTİK!] " : "")}{spellDamage} {spell.DamageType} hasar verdi! (Atış: {roll} + {spellMod + prof} = {total})"
        };
    }
}
