using Godot;
using System.Collections.Generic;
using System.Linq;

public partial class DataManager : Node
{
    public static DataManager Instance { get; private set; }

    private readonly Dictionary<string, Godot.Collections.Dictionary> armor = new();
    private readonly Dictionary<string, ArmorData> armorData = new();

    private readonly Dictionary<string, Godot.Collections.Dictionary> characters = new();
    private readonly Dictionary<string, CharacterData> characterData = new();

    private readonly Dictionary<string, Godot.Collections.Dictionary> conditions = new();
    private readonly Dictionary<string, ConditionData> conditionData = new();

    private readonly Dictionary<string, Godot.Collections.Dictionary> items = new();
    private readonly Dictionary<string, ItemData> itemData = new();

    private readonly Dictionary<string, Godot.Collections.Dictionary> monsters = new();
    private readonly Dictionary<string, MonsterData> monsterData = new();

    private readonly Dictionary<string, Godot.Collections.Dictionary> spells = new();
    private readonly Dictionary<string, SpellData> spellData = new();

    private readonly Dictionary<string, Godot.Collections.Dictionary> weapons = new();
    private readonly Dictionary<string, WeaponData> weaponData = new();

    public override void _Ready()
    {
        Instance = this;
        GD.Print("DataManager başladı.");

        LoadMonsters();
        LoadSpells();
        LoadWeapons();
        LoadArmor();
        LoadConditions();
        LoadItems();
        LoadCharacters();
    }

    // =========================================================
    // MONSTERS
    // =========================================================

    private void LoadMonsters()
    {
        LoadFolder(ResolveFolderPath("res://Data/Monsters/", "res://data/monsters/"), monsters);

        foreach (var pair in monsters)
        {
            MonsterData monster = ConvertMonster(pair.Value);
            if (monster != null)
                monsterData[monster.Id] = monster;
        }

        GD.Print($"Monster sayısı: {monsterData.Count}");
    }

    private MonsterData ConvertMonster(Godot.Collections.Dictionary data)
    {
        var challenge = GetDict(data, "challenge");

        var monster = new MonsterData
        {
            Id = GetString(data, "id"),
            Name = GetString(data, "name", "Unknown Monster"),
            Description = GetString(data, "description"),
            Type = GetString(data, "type", "unknown"),
            Size = GetString(data, "size", "Medium"),
            Alignment = GetString(data, "alignment"),
            ChallengeXP = GetInt(challenge, "xp", 50)
        };

        var abilities = GetDict(data, "ability_scores");
        monster.AbilityScores = ParseAbilityScores(abilities);

        var combat = GetDict(data, "combat");
        var hp = GetDict(combat, "hit_points");
        var speed = GetDict(combat, "speed");
        var initiative = GetDict(combat, "initiative");

        monster.Combat = new MonsterCombat
        {
            ArmorClass = GetInt(combat, "armor_class", 10),
            HitPoints = new MonsterHitPoints
            {
                Average = GetInt(hp, "average", 1),
                Formula = GetString(hp, "formula", "1d8")
            },
            Speed = new MonsterSpeed
            {
                Walking = GetInt(speed, "walking", 30),
                Burrowing = GetInt(speed, "burrowing"),
                Climbing = GetInt(speed, "climbing"),
                Flying = GetInt(speed, "flying"),
                Swimming = GetInt(speed, "swimming")
            },
            InitiativeModifier = GetInt(initiative, "modifier")
        };

        var attacks = GetArray(data, "attacks");
        if (attacks != null)
        {
            foreach (Variant attackItem in attacks)
            {
                if (attackItem.VariantType != Variant.Type.Dictionary)
                    continue;

                var attack = attackItem.AsGodotDictionary();
                monster.Attacks.Add(new MonsterAttackData
                {
                    Name = GetString(attack, "name", "Attack"),
                    WeaponId = GetString(attack, "weapon_id"),
                    AttackBonus = GetInt(attack, "attack_bonus"),
                    DamageDice = GetString(attack, "damage_dice", "1d4"),
                    DamageBonus = GetInt(attack, "damage_bonus"),
                    DamageType = GetString(attack, "damage_type", "bludgeoning")
                });
            }
        }

        return monster;
    }

    public MonsterData GetMonsterData(string id)
    {
        if (id != null && monsterData.TryGetValue(id, out MonsterData monster))
            return monster;

        GD.PrintErr($"Monster bulunamadı: {id}");
        return null;
    }

    public List<string> GetAllMonsterIds() => monsterData.Keys.ToList();

    // =========================================================
    // SPELLS
    // =========================================================

    private void LoadSpells()
    {
        string path = ResolveFolderPath("res://Data/Spells/", "res://data/spells/");
        if (DirAccess.DirExistsAbsolute(path))
        {
            LoadFolder(path, spells);
            foreach (var pair in spells)
            {
                SpellData spell = ConvertSpell(pair.Value);
                if (spell != null)
                    spellData[spell.Id] = spell;
            }
        }

        GD.Print($"Spell sayısı: {spellData.Count}");
    }

    private SpellData ConvertSpell(Godot.Collections.Dictionary data)
    {
        var damage = GetDict(data, "damage");
        var healing = GetDict(data, "healing");

        return new SpellData
        {
            Id = GetString(data, "id"),
            Name = GetString(data, "name", "Unnamed Spell"),
            Description = GetString(data, "description"),
            Level = GetInt(data, "level"),
            School = GetString(data, "school"),
            AttackRequired = GetBool(data, "attack_required"),
            SavingThrowRequired = GetBool(data, "saving_throw_required"),
            SavingThrowAbility = GetString(data, "saving_throw_ability"),
            DamageDice = GetString(damage, "dice"),
            DamageType = GetString(damage, "type", GetString(damage, "damage_type")),
            HealingDice = GetString(healing, "dice"),
            AutoHit = GetBool(data, "auto_hit")
        };
    }

    public SpellData GetSpellData(string id)
    {
        if (id != null && spellData.TryGetValue(id, out SpellData found))
            return found;
        return null;
    }

    public Godot.Collections.Dictionary GetSpell(string id)
    {
        if (id != null && spells.TryGetValue(id, out var spell))
            return spell;

        GD.PrintErr($"Spell bulunamadı: {id}");
        return null;
    }

    public List<string> GetAllSpellIds() => spellData.Keys.ToList();

    // =========================================================
    // CONDITIONS
    // =========================================================

    private void LoadConditions()
    {
        string path = ResolveFolderPath("res://Data/Conditions/", "res://data/conditions/");
        if (DirAccess.DirExistsAbsolute(path))
        {
            LoadFolder(path, conditions);
            foreach (var pair in conditions)
            {
                var cond = new ConditionData
                {
                    Id = GetString(pair.Value, "id"),
                    Name = GetString(pair.Value, "name", pair.Key),
                    Description = GetString(pair.Value, "description"),
                    AttackDisadvantage = GetBool(pair.Value, "attack_disadvantage"),
                    IncomingAttackAdvantage = GetBool(pair.Value, "incoming_attack_advantage"),
                    CannotAct = GetBool(pair.Value, "cannot_act"),
                    SpeedPenalty = GetInt(pair.Value, "speed_penalty")
                };
                conditionData[cond.Id] = cond;
            }
        }

        GD.Print($"Condition sayısı: {conditionData.Count}");
    }

    public ConditionData GetConditionData(string id)
    {
        if (id != null && conditionData.TryGetValue(id.Trim().ToLowerInvariant(), out ConditionData cond))
            return cond;
        return null;
    }

    public List<string> GetAllConditionIds() => conditionData.Keys.ToList();

    // =========================================================
    // WEAPONS
    // =========================================================

    private void LoadWeapons()
    {
        LoadFolder(ResolveFolderPath("res://Data/Weapons/", "res://data/weapons/"), weapons);

        foreach (var pair in weapons)
        {
            WeaponData weapon = ConvertWeapon(pair.Value);
            if (weapon != null)
                weaponData[weapon.Id] = weapon;
        }

        GD.Print($"Weapon sayısı: {weaponData.Count}");
    }

    private WeaponData ConvertWeapon(Godot.Collections.Dictionary data)
    {
        var damage = GetDict(data, "damage");
        var properties = GetDict(data, "properties");
        var attack = GetDict(data, "attack");
        var mastery = GetDict(data, "mastery");

        string ability = GetString(data, "ability");
        if (string.IsNullOrEmpty(ability))
            ability = GetString(attack, "ability", "strength");

        bool finesse = GetBool(properties, "finesse") || GetBool(attack, "finesse");
        string masteryName = GetString(mastery, "name", GetString(mastery, "property"));

        return new WeaponData
        {
            Id = GetString(data, "id"),
            Name = GetString(data, "name", "Unnamed Weapon"),
            Category = GetString(data, "category", "simple_melee"),
            DamageDice = GetString(damage, "dice", "1d4"),
            DamageType = GetString(damage, "type", GetString(damage, "damage_type", "bludgeoning")),
            Ability = ability,
            Finesse = finesse,
            Mastery = masteryName
        };
    }

    public WeaponData GetWeaponData(string id)
    {
        if (id != null && weaponData.TryGetValue(id, out WeaponData weapon))
            return weapon;

        GD.PrintErr($"Weapon bulunamadı: {id}");
        return null;
    }

    public List<string> GetAllWeaponIds() => weaponData.Keys.ToList();

    // =========================================================
    // ARMOR
    // =========================================================

    private void LoadArmor()
    {
        LoadFolder(ResolveFolderPath("res://Data/armor/", "res://Data/Armor/", "res://data/armor/"), armor);

        foreach (var pair in armor)
        {
            ArmorData armorItem = ConvertArmor(pair.Value);
            if (armorItem != null)
                armorData[armorItem.Id] = armorItem;
        }

        GD.Print($"Armor sayısı: {armorData.Count}");
    }

    private ArmorData ConvertArmor(Godot.Collections.Dictionary data)
    {
        int acValue = 10;
        int dexCap = 0;

        if (data.TryGetValue("armor_class", out Variant acVariant))
        {
            if (acVariant.VariantType == Variant.Type.Dictionary)
            {
                var acDict = acVariant.AsGodotDictionary();
                acValue = GetInt(acDict, "base", 10);
                var dexBonus = GetDict(acDict, "dexterity_bonus");
                if (dexBonus != null && dexBonus.TryGetValue("maximum", out Variant maxVar) && maxVar.VariantType != Variant.Type.Nil)
                    dexCap = maxVar.AsInt32();
            }
            else
            {
                acValue = acVariant.AsInt32();
                dexCap = GetInt(data, "dexterity_bonus_cap");
            }
        }

        var stealth = GetDict(data, "stealth");
        bool stealthDisadv = GetBool(data, "stealth_disadvantage") || GetBool(stealth, "disadvantage");

        return new ArmorData
        {
            Id = GetString(data, "id"),
            Name = GetString(data, "name", "Unnamed Armor"),
            Category = GetString(data, "category", "light"),
            ArmorClass = acValue,
            ArmorType = GetString(data, "armor_type", GetString(data, "category", "light")),
            DexterityBonusCap = dexCap,
            StealthDisadvantage = stealthDisadv
        };
    }

    public ArmorData GetArmorData(string id)
    {
        if (id != null && armorData.TryGetValue(id, out ArmorData found))
            return found;

        return null;
    }

    public List<string> GetAllArmorIds() => armorData.Keys.ToList();

    // =========================================================
    // ITEMS
    // =========================================================

    private void LoadItems()
    {
        string path = ResolveFolderPath("res://Data/Items/", "res://data/items/");
        if (DirAccess.DirExistsAbsolute(path))
        {
            LoadFolder(path, items);
            foreach (var pair in items)
            {
                var item = new ItemData
                {
                    Id = GetString(pair.Value, "id"),
                    Name = GetString(pair.Value, "name", pair.Key),
                    Description = GetString(pair.Value, "description"),
                    Category = GetString(pair.Value, "category", "gear"),
                    Consumable = GetBool(pair.Value, "consumable"),
                    HealingDice = GetString(pair.Value, "healing_dice"),
                    ValueGP = GetInt(pair.Value, "value_gp")
                };
                itemData[item.Id] = item;
            }
        }

        GD.Print($"Item sayısı: {itemData.Count}");
    }

    public ItemData GetItemData(string id)
    {
        if (id != null && itemData.TryGetValue(id.Trim().ToLowerInvariant(), out ItemData found))
            return found;
        return null;
    }

    public List<string> GetAllItemIds() => itemData.Keys.ToList();

    // =========================================================
    // CHARACTERS
    // =========================================================

    private void LoadCharacters()
    {
        LoadFolder(ResolveFolderPath("res://Data/Characters/", "res://data/characters/"), characters);

        foreach (var pair in characters)
        {
            CharacterData character = ConvertCharacter(pair.Value);
            if (character != null)
                characterData[character.Id] = character;
        }

        GD.Print($"Character sayısı: {characterData.Count}");
    }

    private CharacterData ConvertCharacter(Godot.Collections.Dictionary data)
    {
        var character = new CharacterData();

        var identity = GetDict(data, "identity");
        character.Id = GetString(identity, "id", GetString(data, "id"));
        character.Name = GetString(identity, "name", "Kahraman");

        var classData = GetDict(data, "class");
        character.ClassId = GetString(classData, "id", "fighter");
        character.Level = GetInt(classData, "level", 1);

        var progression = GetDict(data, "progression");
        character.ExperiencePoints = GetInt(progression, "xp", 0);

        var abilities = GetDict(data, "ability_scores");
        character.AbilityScores = ParseAbilityScores(abilities);

        var inventory = GetDict(data, "inventory");
        if (inventory != null)
        {
            var weaponArray = GetArray(inventory, "weapons");
            if (weaponArray != null)
            {
                foreach (Variant w in weaponArray)
                    character.WeaponInventory.Add(w.AsString());
            }

            var armorArray = GetArray(inventory, "armor");
            if (armorArray != null)
            {
                foreach (Variant a in armorArray)
                    character.ArmorInventory.Add(a.AsString());
            }

            character.EquippedWeaponId = GetString(inventory, "equipped_weapon");
            character.EquippedArmorId = GetString(inventory, "equipped_armor");
            character.EquippedShieldId = GetString(inventory, "equipped_shield");
            character.HasShieldInInventory = GetBool(inventory, "has_shield") || !string.IsNullOrEmpty(character.EquippedShieldId);
            character.Gold = GetInt(inventory, "gold", 15);

            var itemsDict = GetDict(inventory, "items");
            if (itemsDict != null)
            {
                foreach (var itemPair in itemsDict)
                    character.AddItem(itemPair.Key.AsString(), itemPair.Value.AsInt32());
            }
        }

        var combat = GetDict(data, "combat");
        var hitPoints = GetDict(combat, "hit_points") ?? GetDict(combat, "hp");
        character.MaximumHP = GetInt(hitPoints, "maximum", 10);
        character.CurrentHP = GetInt(hitPoints, "current", character.MaximumHP);

        var hitDice = GetDict(combat, "hit_dice");
        character.HitDieSides = GetInt(hitDice, "sides", 10);
        character.HitDiceRemaining = GetInt(hitDice, "current", character.Level);

        ArmorData equippedArmor = !string.IsNullOrEmpty(character.EquippedArmorId)
            ? GetArmorData(character.EquippedArmorId)
            : null;
        character.ArmorClass = ArmorClassCalculator.Calculate(character, equippedArmor);

        character.InitiativeBonus = GetInt(
            combat,
            "initiative_bonus",
            AbilityScoreCalculator.GetModifier(character.AbilityScores.Dexterity)
        );

        var speed = GetDict(combat, "speed");
        character.WalkingSpeed = GetInt(speed, "walking", 30);

        var skills = GetDict(data, "skills");
        if (skills != null)
        {
            foreach (var skillPair in skills)
            {
                string skillName = skillPair.Key.AsString().ToLowerInvariant();
                if (skillPair.Value.VariantType != Variant.Type.Dictionary)
                    continue;

                var skillDict = skillPair.Value.AsGodotDictionary();
                character.Skills[skillName] = new SkillData
                {
                    Proficient = GetBool(skillDict, "proficient"),
                    Expertise = GetBool(skillDict, "expertise")
                };
            }
        }

        var savingThrows = GetDict(data, "saving_throws");
        if (savingThrows != null)
        {
            foreach (var stPair in savingThrows)
            {
                string abilityName = stPair.Key.AsString().ToLowerInvariant();
                if (stPair.Value.VariantType != Variant.Type.Dictionary)
                    continue;

                var stDict = stPair.Value.AsGodotDictionary();
                character.SavingThrows[abilityName] = new SavingThrowData
                {
                    Proficient = GetBool(stDict, "proficient")
                };
            }
        }

        return character;
    }

    public CharacterData GetCharacterData(string id)
    {
        if (id != null && characterData.TryGetValue(id, out CharacterData character))
            return character;

        GD.PrintErr($"Character bulunamadı: {id}");
        return null;
    }

    public List<string> GetAllCharacterIds() => characterData.Keys.ToList();

    // =========================================================
    // HELPERS & GENERIC JSON LOADER
    // =========================================================

    private static AbilityScores ParseAbilityScores(Godot.Collections.Dictionary abilities)
    {
        return new AbilityScores
        {
            Strength = GetInt(abilities, "strength", 10),
            Dexterity = GetInt(abilities, "dexterity", 10),
            Constitution = GetInt(abilities, "constitution", 10),
            Intelligence = GetInt(abilities, "intelligence", 10),
            Wisdom = GetInt(abilities, "wisdom", 10),
            Charisma = GetInt(abilities, "charisma", 10)
        };
    }

    private static Godot.Collections.Dictionary GetDict(Godot.Collections.Dictionary dict, string key)
    {
        if (dict != null && dict.TryGetValue(key, out Variant val) && val.VariantType == Variant.Type.Dictionary)
            return val.AsGodotDictionary();
        return null;
    }

    private static Godot.Collections.Array GetArray(Godot.Collections.Dictionary dict, string key)
    {
        if (dict != null && dict.TryGetValue(key, out Variant val) && val.VariantType == Variant.Type.Array)
            return val.AsGodotArray();
        return null;
    }

    private static string GetString(Godot.Collections.Dictionary dict, string key, string defaultValue = "")
    {
        if (dict != null && dict.TryGetValue(key, out Variant val) && val.VariantType != Variant.Type.Nil)
            return val.AsString();
        return defaultValue;
    }

    private static int GetInt(Godot.Collections.Dictionary dict, string key, int defaultValue = 0)
    {
        if (dict != null && dict.TryGetValue(key, out Variant val) && val.VariantType != Variant.Type.Nil)
            return val.AsInt32();
        return defaultValue;
    }

    private static bool GetBool(Godot.Collections.Dictionary dict, string key, bool defaultValue = false)
    {
        if (dict != null && dict.TryGetValue(key, out Variant val) && val.VariantType != Variant.Type.Nil)
            return val.AsBool();
        return defaultValue;
    }

    private static string ResolveFolderPath(params string[] candidates)
    {
        foreach (string candidate in candidates)
        {
            if (DirAccess.DirExistsAbsolute(candidate))
                return candidate;
        }

        return candidates[0];
    }

    private void LoadFolder(
        string path,
        Dictionary<string, Godot.Collections.Dictionary> target)
    {
        DirAccess dir = DirAccess.Open(path);
        if (dir == null)
        {
            GD.PrintErr($"Klasör bulunamadı: {path}");
            return;
        }

        dir.ListDirBegin();

        while (true)
        {
            string fileName = dir.GetNext();
            if (fileName == "")
                break;

            if (dir.CurrentIsDir() || !fileName.EndsWith(".json"))
                continue;

            LoadJsonFile(path + fileName, target);
        }

        dir.ListDirEnd();
    }

    private void LoadJsonFile(
        string filePath,
        Dictionary<string, Godot.Collections.Dictionary> target)
    {
        FileAccess file = FileAccess.Open(filePath, FileAccess.ModeFlags.Read);
        if (file == null)
        {
            GD.PrintErr($"❌ DOSYA AÇILAMADI: {filePath}");
            return;
        }

        string jsonText = file.GetAsText();
        file.Close();

        var json = new Json();
        Error error = json.Parse(jsonText);
        if (error != Error.Ok)
        {
            GD.PrintErr($"❌ JSON HATASI ({filePath}, satır {json.GetErrorLine()}): {json.GetErrorMessage()}");
            return;
        }

        Variant data = json.Data;
        if (data.VariantType != Variant.Type.Dictionary)
        {
            GD.PrintErr($"❌ JSON Dictionary değil: {filePath}");
            return;
        }

        Godot.Collections.Dictionary dictionary = data.AsGodotDictionary();
        string id = GetString(dictionary, "id");
        if (string.IsNullOrEmpty(id))
        {
            var identity = GetDict(dictionary, "identity");
            id = GetString(identity, "id");
        }

        if (string.IsNullOrEmpty(id))
        {
            GD.PrintErr($"❌ JSON İÇİNDE id YOK: {filePath}");
            return;
        }

        target[id] = dictionary;
        GD.Print($"✅ YÜKLENDİ: {id}");
    }
}
