using System;

public static class ArmorClassCalculator
{
    public static int Calculate(CharacterData character, ArmorData armor)
    {
        int dexterityModifier = AbilityScoreCalculator.GetModifier(character.AbilityScores.Dexterity);
        int shieldBonus = string.IsNullOrWhiteSpace(character.EquippedShieldId) ? 0 : 2;

        if (armor == null)
            return 10 + dexterityModifier + shieldBonus;

        string armorType = (armor.ArmorType ?? armor.Category ?? "").Trim().ToLowerInvariant();
        int baseAc = armor.ArmorClass;

        int dexContribution = armorType switch
        {
            "light" => dexterityModifier,
            "medium" => Math.Min(dexterityModifier, armor.DexterityBonusCap > 0 ? armor.DexterityBonusCap : 2),
            "heavy" => 0,
            _ => 0
        };

        return baseAc + dexContribution + shieldBonus;
    }
}
