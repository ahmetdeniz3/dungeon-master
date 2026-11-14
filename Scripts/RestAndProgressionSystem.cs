using System;
using System.Collections.Generic;

public class RestResult
{
    public bool IsLongRest { get; set; }
    public int HealedHP { get; set; }
    public int HitDiceSpent { get; set; }
    public int HitDiceRestored { get; set; }
    public string Summary { get; set; }
}

public class DeathSaveResult
{
    public int Roll { get; set; }
    public bool Recovered { get; set; }
    public bool Stabilized { get; set; }
    public bool Dead { get; set; }
    public string Summary { get; set; }
}

public static class RestAndProgressionSystem
{
    private static readonly int[] XpThresholds =
    {
        0,      // Lv 1
        300,    // Lv 2
        900,    // Lv 3
        2700,   // Lv 4
        6500,   // Lv 5
        14000,  // Lv 6
        23000,  // Lv 7
        34000,  // Lv 8
        48000,  // Lv 9
        64000,  // Lv 10
        85000,  // Lv 11
        100000, // Lv 12
        120000, // Lv 13
        140000, // Lv 14
        165000, // Lv 15
        195000, // Lv 16
        225000, // Lv 17
        265000, // Lv 18
        305000, // Lv 19
        355000  // Lv 20
    };

    public static int GetNextLevelXP(int currentLevel)
    {
        int idx = Math.Clamp(currentLevel, 1, XpThresholds.Length - 1);
        return XpThresholds[idx];
    }

    public static int GrantExperience(CharacterData character, int xpGained)
    {
        if (character == null || xpGained <= 0)
            return 0;

        character.ExperiencePoints += xpGained;
        int levelsGained = 0;

        while (character.Level < 20 && character.ExperiencePoints >= XpThresholds[character.Level])
        {
            character.Level++;
            levelsGained++;

            int conMod = AbilityScoreCalculator.GetModifier(character.AbilityScores.Constitution);
            int hpGain = Math.Max(1, (character.HitDieSides / 2 + 1) + conMod);
            character.MaximumHP += hpGain;
            character.CurrentHP += hpGain;
            character.HitDiceRemaining = Math.Min(character.Level, character.HitDiceRemaining + 1);
        }

        return levelsGained;
    }

    public static RestResult PerformShortRest(CharacterData character)
    {
        if (character == null)
            return null;

        if (character.CurrentHP >= character.MaximumHP)
        {
            return new RestResult
            {
                IsLongRest = false,
                Summary = $"{character.Name} kısa dinlenme yaptı (HP zaten dolu: {character.CurrentHP}/{character.MaximumHP})."
            };
        }

        if (character.HitDiceRemaining <= 0)
        {
            return new RestResult
            {
                IsLongRest = false,
                Summary = $"{character.Name} kısa dinlenme yaptı ama harcanacak Hit Die kalmadı."
            };
        }

        int conMod = AbilityScoreCalculator.GetModifier(character.AbilityScores.Constitution);
        int roll = DiceRoller.Roll(1, character.HitDieSides);
        int heal = Math.Max(1, roll + conMod);

        int before = character.CurrentHP;
        character.CurrentHP = Math.Min(character.MaximumHP, character.CurrentHP + heal);
        character.HitDiceRemaining--;
        character.DeathSaveSuccesses = 0;
        character.DeathSaveFailures = 0;

        int actualHealed = character.CurrentHP - before;
        return new RestResult
        {
            IsLongRest = false,
            HealedHP = actualHealed,
            HitDiceSpent = 1,
            Summary = $"⛺ Kısa Dinlenme: 1d{character.HitDieSides}({roll}) {(conMod >= 0 ? "+" : "")}{conMod} -> +{actualHealed} HP ({character.CurrentHP}/{character.MaximumHP}), Kalan Hit Dice: {character.HitDiceRemaining}/{character.Level}"
        };
    }

    public static RestResult PerformLongRest(CharacterData character)
    {
        if (character == null)
            return null;

        int beforeHp = character.CurrentHP;
        character.CurrentHP = character.MaximumHP;

        int restoreDice = Math.Max(1, character.Level / 2);
        int beforeDice = character.HitDiceRemaining;
        character.HitDiceRemaining = Math.Min(character.Level, character.HitDiceRemaining + restoreDice);

        character.DeathSaveSuccesses = 0;
        character.DeathSaveFailures = 0;
        character.NextAttackDisadvantage = false;
        character.ActiveConditions.Clear();

        foreach (int slotLevel in new List<int>(character.SpellSlotsMax.Keys))
            character.SpellSlotsCurrent[slotLevel] = character.SpellSlotsMax[slotLevel];

        return new RestResult
        {
            IsLongRest = true,
            HealedHP = character.CurrentHP - beforeHp,
            HitDiceRestored = character.HitDiceRemaining - beforeDice,
            Summary = $"🌙 Uzun Dinlenme tamamlandı: HP dolu ({character.CurrentHP}/{character.MaximumHP}), Hit Dice: {character.HitDiceRemaining}/{character.Level}, durumlar temizlendi."
        };
    }

    public static DeathSaveResult RollDeathSave(CharacterData character)
    {
        if (character == null)
            return null;

        if (character.CurrentHP > 0)
        {
            return new DeathSaveResult
            {
                Summary = $"{character.Name} zaten hayatta ({character.CurrentHP} HP)."
            };
        }

        int roll = DiceRoller.RollD20();
        var result = new DeathSaveResult { Roll = roll };

        if (roll == 20)
        {
            character.CurrentHP = 1;
            character.DeathSaveSuccesses = 0;
            character.DeathSaveFailures = 0;
            result.Recovered = true;
            result.Summary = $"✨ Ölüm Kurtarma Zarı: d20(20)! {character.Name} 1 HP ile bilincini geri kazandı!";
            return result;
        }

        if (roll == 1)
        {
            character.DeathSaveFailures = Math.Min(3, character.DeathSaveFailures + 2);
        }
        else if (roll >= 10)
        {
            character.DeathSaveSuccesses = Math.Min(3, character.DeathSaveSuccesses + 1);
        }
        else
        {
            character.DeathSaveFailures = Math.Min(3, character.DeathSaveFailures + 1);
        }

        if (character.DeathSaveFailures >= 3)
        {
            result.Dead = true;
            result.Summary = $"💀 Ölüm Kurtarma Zarı: d20({roll}) -> 3 başarısızlık! {character.Name} öldü.";
        }
        else if (character.DeathSaveSuccesses >= 3)
        {
            character.CurrentHP = 1;
            character.DeathSaveSuccesses = 0;
            character.DeathSaveFailures = 0;
            result.Stabilized = true;
            result.Summary = $"🩹 Ölüm Kurtarma Zarı: d20({roll}) -> 3 başarı! {character.Name} stabilize oldu (1 HP).";
        }
        else
        {
            result.Summary = $"🎲 Ölüm Kurtarma Zarı: d20({roll}) | Başarı: {character.DeathSaveSuccesses}/3, Başarısızlık: {character.DeathSaveFailures}/3";
        }

        return result;
    }
}
