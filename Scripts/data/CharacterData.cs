using System.Collections.Generic;

public class CharacterData
{
    public string Id { get; set; }
    public string Name { get; set; }
    public string ClassId { get; set; } = "fighter";
    public int Level { get; set; } = 1;
    public int ExperiencePoints { get; set; } = 0;

    public string EquippedArmorId { get; set; }
    public string EquippedShieldId { get; set; }
    public bool HasShieldInInventory { get; set; }
    public List<string> ArmorInventory { get; set; } = new();

    public List<string> WeaponInventory { get; set; } = new();
    public string EquippedWeaponId { get; set; }

    public int Gold { get; set; } = 15;
    public Dictionary<string, int> Items { get; set; } = new();

    public AbilityScores AbilityScores { get; set; } = new();

    public Dictionary<string, SkillData> Skills { get; set; } = new();
    public Dictionary<string, SavingThrowData> SavingThrows { get; set; } = new();

    // Combat & Rest
    public int MaximumHP { get; set; }
    public int CurrentHP { get; set; }
    public int ArmorClass { get; set; }
    public int InitiativeBonus { get; set; }
    public int WalkingSpeed { get; set; } = 30;
    public int HitDieSides { get; set; } = 10;
    public int HitDiceRemaining { get; set; } = 1;

    // Death Saves & Status
    public int DeathSaveSuccesses { get; set; }
    public int DeathSaveFailures { get; set; }
    public bool NextAttackDisadvantage { get; set; }
    public bool IsDodging { get; set; }
    public List<string> ActiveConditions { get; set; } = new();

    // Spells
    public string SpellcastingAbility { get; set; } = "intelligence";
    public List<string> KnownSpells { get; set; } = new();
    public Dictionary<int, int> SpellSlotsMax { get; set; } = new();
    public Dictionary<int, int> SpellSlotsCurrent { get; set; } = new();

    public void AddItem(string itemId, int quantity = 1)
    {
        if (string.IsNullOrWhiteSpace(itemId) || quantity <= 0)
            return;

        string key = itemId.Trim().ToLowerInvariant();
        Items[key] = Items.TryGetValue(key, out int cur) ? cur + quantity : quantity;
    }

    public bool ConsumeItem(string itemId, int quantity = 1)
    {
        if (string.IsNullOrWhiteSpace(itemId) || quantity <= 0)
            return false;

        string key = itemId.Trim().ToLowerInvariant();
        if (!Items.TryGetValue(key, out int cur) || cur < quantity)
            return false;

        if (cur == quantity)
            Items.Remove(key);
        else
            Items[key] = cur - quantity;

        return true;
    }

    public int GetItemCount(string itemId)
    {
        if (string.IsNullOrWhiteSpace(itemId))
            return 0;

        string key = itemId.Trim().ToLowerInvariant();
        return Items.TryGetValue(key, out int count) ? count : 0;
    }
}
