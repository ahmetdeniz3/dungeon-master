using System;
using System.Collections.Generic;

public static class SkillCalculator
{
    private static readonly Dictionary<string, string> SkillToAbility = new(StringComparer.OrdinalIgnoreCase)
    {
        { "acrobatics", "dexterity" },
        { "animal_handling", "wisdom" },
        { "arcana", "intelligence" },
        { "athletics", "strength" },
        { "deception", "charisma" },
        { "history", "intelligence" },
        { "insight", "wisdom" },
        { "intimidation", "charisma" },
        { "investigation", "intelligence" },
        { "medicine", "wisdom" },
        { "nature", "intelligence" },
        { "perception", "wisdom" },
        { "performance", "charisma" },
        { "persuasion", "charisma" },
        { "religion", "intelligence" },
        { "sleight_of_hand", "dexterity" },
        { "stealth", "dexterity" },
        { "survival", "wisdom" }
    };

    public static IReadOnlyCollection<string> AllSkills => SkillToAbility.Keys;

    public static string GetAbilityForSkill(string skillName)
    {
        if (string.IsNullOrWhiteSpace(skillName))
            return "strength";

        string key = skillName.Trim().ToLowerInvariant().Replace(' ', '_');
        return SkillToAbility.TryGetValue(key, out string ability) ? ability : "strength";
    }

    public static bool IsKnownSkill(string skillName)
    {
        if (string.IsNullOrWhiteSpace(skillName))
            return false;

        string key = skillName.Trim().ToLowerInvariant().Replace(' ', '_');
        return SkillToAbility.ContainsKey(key);
    }

    public static int GetAbilityScoreByName(AbilityScores scores, string abilityName)
    {
        if (scores == null)
            return 10;

        return (abilityName ?? "").Trim().ToLowerInvariant() switch
        {
            "strength" or "str" => scores.Strength,
            "dexterity" or "dex" => scores.Dexterity,
            "constitution" or "con" => scores.Constitution,
            "intelligence" or "int" => scores.Intelligence,
            "wisdom" or "wis" => scores.Wisdom,
            "charisma" or "cha" => scores.Charisma,
            _ => 10
        };
    }

    public static int GetSkillModifier(
        int abilityScore,
        int characterLevel,
        SkillData skill)
    {
        int abilityModifier = AbilityScoreCalculator.GetModifier(abilityScore);
        int proficiencyBonus = ProficiencyCalculator.GetBonus(characterLevel);
        int modifier = abilityModifier;

        if (skill != null)
        {
            if (skill.Proficient)
                modifier += proficiencyBonus;

            if (skill.Expertise)
                modifier += proficiencyBonus;
        }

        return modifier;
    }
}
