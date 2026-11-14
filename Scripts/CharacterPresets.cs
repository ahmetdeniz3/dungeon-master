using System.Collections.Generic;

public static class CharacterPresets
{
    public static CharacterData CreateByPreset(string name, string classId)
    {
        var ch = new CharacterData
        {
            Id = "custom_hero",
            Name = name,
            ClassId = classId,
            Level = 1,
            WalkingSpeed = 30,
            Gold = 15
        };

        ch.AddItem("healing_potion", 2);
        ch.AddItem("torch", 2);
        ch.AddItem("ration", 2);

        if (classId == "wizard")
        {
            ch.AbilityScores = new AbilityScores { Strength = 8, Dexterity = 14, Constitution = 12, Intelligence = 16, Wisdom = 12, Charisma = 10 };
            ch.HitDieSides = 6;
            ch.MaximumHP = 7;
            ch.WeaponInventory = new List<string> { "dagger" };
            ch.EquippedWeaponId = "dagger";
            ch.ArmorInventory = new List<string>();
            ch.EquippedArmorId = "";
            ch.SpellcastingAbility = "intelligence";
            ch.KnownSpells = new List<string> { "fire_bolt", "magic_missile", "cure_wounds" };
            ch.SpellSlotsMax[1] = 2;
            ch.SpellSlotsCurrent[1] = 2;
            ch.Skills["arcana"] = new SkillData { Proficient = true };
            ch.Skills["investigation"] = new SkillData { Proficient = true };
            ch.SavingThrows["intelligence"] = new SavingThrowData { Proficient = true };
            ch.SavingThrows["wisdom"] = new SavingThrowData { Proficient = true };
        }
        else if (classId == "rogue")
        {
            ch.AbilityScores = new AbilityScores { Strength = 10, Dexterity = 16, Constitution = 12, Intelligence = 12, Wisdom = 12, Charisma = 12 };
            ch.HitDieSides = 8;
            ch.MaximumHP = 9;
            ch.WeaponInventory = new List<string> { "scimitar", "dagger", "shortbow" };
            ch.EquippedWeaponId = "scimitar";
            ch.ArmorInventory = new List<string> { "leather_armor" };
            ch.EquippedArmorId = "leather_armor";
            ch.AddItem("thieves_tools", 1);
            ch.Skills["stealth"] = new SkillData { Proficient = true, Expertise = true };
            ch.Skills["acrobatics"] = new SkillData { Proficient = true, Expertise = true };
            ch.Skills["perception"] = new SkillData { Proficient = true };
            ch.SavingThrows["dexterity"] = new SavingThrowData { Proficient = true };
            ch.SavingThrows["intelligence"] = new SavingThrowData { Proficient = true };
        }
        else
        {
            ch.AbilityScores = new AbilityScores { Strength = 16, Dexterity = 14, Constitution = 14, Intelligence = 10, Wisdom = 10, Charisma = 8 };
            ch.HitDieSides = 10;
            ch.MaximumHP = 12;
            ch.WeaponInventory = new List<string> { "longsword", "shortbow", "dagger" };
            ch.EquippedWeaponId = "longsword";
            ch.ArmorInventory = new List<string> { "chain_shirt", "leather_armor", "chain_mail" };
            ch.EquippedArmorId = "chain_shirt";
            ch.HasShieldInInventory = true;
            ch.EquippedShieldId = "shield";
            ch.Skills["athletics"] = new SkillData { Proficient = true };
            ch.Skills["perception"] = new SkillData { Proficient = true };
            ch.SavingThrows["strength"] = new SavingThrowData { Proficient = true };
            ch.SavingThrows["constitution"] = new SavingThrowData { Proficient = true };
        }

        ch.CurrentHP = ch.MaximumHP;
        ch.HitDiceRemaining = 1;
        ch.InitiativeBonus = AbilityScoreCalculator.GetModifier(ch.AbilityScores.Dexterity);

        ArmorData armor = !string.IsNullOrEmpty(ch.EquippedArmorId)
            ? DataManager.Instance?.GetArmorData(ch.EquippedArmorId)
            : null;
        ch.ArmorClass = ArmorClassCalculator.Calculate(ch, armor);
        return ch;
    }
}
