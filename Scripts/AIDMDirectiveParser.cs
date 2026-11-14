using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

public class PendingRollRequest
{
    public bool IsSavingThrow { get; set; }
    public string TargetName { get; set; }
    public int DifficultyClass { get; set; }
}

public class AIDMParseResult
{
    public string CleanNarrative { get; set; }
    public PendingRollRequest RollRequest { get; set; }
    public List<string> CombatEnemies { get; set; } = new();
    public List<string> Memories { get; set; } = new();
    public List<string> AddConditions { get; set; } = new();
    public List<string> RemoveConditions { get; set; } = new();
    public List<string> AddItems { get; set; } = new();
    public List<string> RemoveItems { get; set; } = new();
    public List<string> AddQuests { get; set; } = new();
    public List<string> CompleteQuests { get; set; } = new();
    public string LocationChange { get; set; } = "";
    public string SceneUpdate { get; set; } = "";
    public int GoldDelta { get; set; }
    public string RestType { get; set; } = "";
    public int DamageAmount { get; set; }
    public int HealAmount { get; set; }
}

public static class AIDMDirectiveParser
{
    private static readonly Regex TagRegex = new(
        @"\[(CHECK|BECER[İI]\s*KONTROL[ÜU]|SAVE|KURTARMA\s*ZARI|COMBAT|SAVA[ŞS]|DAMAGE|HASAR|HEAL|[İI]Y[İI]LE[ŞS]ME|MEMORY|FACT|HAFIZA|CONDITION|DURUM|REST|D[İI]NLENME|GOLD|ALTIN|ITEM|E[ŞS]YA|QUEST|G[ÖO]REV|LOCATION|KONUM|SCENE|SAHNE)\s*:\s*([^\]]+)\]",
        RegexOptions.IgnoreCase | RegexOptions.Compiled
    );

    private static readonly Regex MetaNoteRegex = new(
        @"\((?:Mevcut|Valid|Kullan[ıi]labilir)[^)]*\)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled
    );

    public static AIDMParseResult Parse(string rawResponse)
    {
        var result = new AIDMParseResult();
        if (string.IsNullOrWhiteSpace(rawResponse))
        {
            result.CleanNarrative = "";
            return result;
        }

        foreach (Match match in TagRegex.Matches(rawResponse))
        {
            string command = NormalizeCommand(match.Groups[1].Value);
            string payload = match.Groups[2].Value.Trim();

            if (command == "MEMORY")
            {
                if (!string.IsNullOrWhiteSpace(payload))
                    result.Memories.Add(payload.Trim());
                continue;
            }

            if (command == "LOCATION")
            {
                if (!string.IsNullOrWhiteSpace(payload))
                    result.LocationChange = payload.Trim();
                continue;
            }

            if (command == "SCENE")
            {
                if (!string.IsNullOrWhiteSpace(payload))
                    result.SceneUpdate = payload.Trim();
                continue;
            }

            var args = ParseKeyValues(payload);

            switch (command)
            {
                case "CHECK":
                    {
                        string skill = args.TryGetValue("skill", out string s) ? s : "perception";
                        int dc = args.TryGetValue("dc", out string dcStr) && int.TryParse(dcStr, out int parsedDc) ? parsedDc : 12;
                        result.RollRequest = new PendingRollRequest
                        {
                            IsSavingThrow = false,
                            TargetName = skill.Trim().ToLowerInvariant().Replace(' ', '_'),
                            DifficultyClass = dc
                        };
                        break;
                    }
                case "SAVE":
                    {
                        string ability = args.TryGetValue("ability", out string a) ? a : "dexterity";
                        int dc = args.TryGetValue("dc", out string dcStr) && int.TryParse(dcStr, out int parsedDc) ? parsedDc : 12;
                        result.RollRequest = new PendingRollRequest
                        {
                            IsSavingThrow = true,
                            TargetName = ability.Trim().ToLowerInvariant(),
                            DifficultyClass = dc
                        };
                        break;
                    }
                case "COMBAT":
                    {
                        if (args.TryGetValue("enemies", out string enemiesStr))
                        {
                            string[] items = enemiesStr.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                            foreach (string item in items)
                                result.CombatEnemies.Add(item.ToLowerInvariant());
                        }
                        break;
                    }
                case "DAMAGE":
                    {
                        if (args.TryGetValue("amount", out string amtStr) && int.TryParse(amtStr, out int dmg) && dmg > 0)
                            result.DamageAmount += dmg;
                        break;
                    }
                case "HEAL":
                    {
                        if (args.TryGetValue("amount", out string amtStr) && int.TryParse(amtStr, out int heal) && heal > 0)
                            result.HealAmount += heal;
                        break;
                    }
                case "CONDITION":
                    {
                        if (args.TryGetValue("add", out string addCond) && !string.IsNullOrWhiteSpace(addCond))
                            result.AddConditions.Add(addCond.Trim().ToLowerInvariant());
                        if (args.TryGetValue("remove", out string remCond) && !string.IsNullOrWhiteSpace(remCond))
                            result.RemoveConditions.Add(remCond.Trim().ToLowerInvariant());
                        break;
                    }
                case "ITEM":
                    {
                        if (args.TryGetValue("add", out string addItem) && !string.IsNullOrWhiteSpace(addItem))
                            result.AddItems.Add(addItem.Trim().ToLowerInvariant());
                        if (args.TryGetValue("remove", out string remItem) && !string.IsNullOrWhiteSpace(remItem))
                            result.RemoveItems.Add(remItem.Trim().ToLowerInvariant());
                        break;
                    }
                case "GOLD":
                    {
                        if ((args.TryGetValue("delta", out string gStr) || args.TryGetValue("amount", out gStr)) &&
                            int.TryParse(gStr, out int goldVal))
                            result.GoldDelta += goldVal;
                        break;
                    }
                case "QUEST":
                    {
                        string qVal;
                        if (payload.StartsWith("add=", StringComparison.OrdinalIgnoreCase))
                        {
                            qVal = payload.Substring(4).Trim().Trim('"', '\'');
                            if (!string.IsNullOrWhiteSpace(qVal))
                                result.AddQuests.Add(qVal);
                        }
                        else if (payload.StartsWith("complete=", StringComparison.OrdinalIgnoreCase))
                        {
                            qVal = payload.Substring(9).Trim().Trim('"', '\'');
                            if (!string.IsNullOrWhiteSpace(qVal))
                                result.CompleteQuests.Add(qVal);
                        }
                        else
                        {
                            qVal = payload.Trim().Trim('"', '\'');
                            if (!string.IsNullOrWhiteSpace(qVal))
                                result.AddQuests.Add(qVal);
                        }
                        break;
                    }
                case "REST":
                    {
                        if (args.TryGetValue("type", out string restType))
                            result.RestType = restType.Trim().ToLowerInvariant();
                        break;
                    }
            }
        }

        string withoutTags = TagRegex.Replace(rawResponse, "");
        result.CleanNarrative = MetaNoteRegex.Replace(withoutTags, "").Trim();
        return result;
    }

    private static string NormalizeCommand(string raw)
    {
        string upper = raw.Trim().ToUpperInvariant()
            .Replace('İ', 'I')
            .Replace('Ü', 'U')
            .Replace('Ş', 'S')
            .Replace('Ö', 'O');

        if (upper.StartsWith("BECERI"))
            return "CHECK";
        if (upper.StartsWith("KURTARMA"))
            return "SAVE";
        if (upper.StartsWith("SAVAS"))
            return "COMBAT";
        if (upper == "HASAR")
            return "DAMAGE";
        if (upper.StartsWith("IYILESME"))
            return "HEAL";
        if (upper == "FACT" || upper == "HAFIZA")
            return "MEMORY";
        if (upper == "DURUM")
            return "CONDITION";
        if (upper == "DINLENME")
            return "REST";
        if (upper == "ALTIN")
            return "GOLD";
        if (upper == "ESYA")
            return "ITEM";
        if (upper == "GOREV")
            return "QUEST";
        if (upper == "KONUM")
            return "LOCATION";
        if (upper == "SAHNE")
            return "SCENE";

        return upper;
    }

    private static Dictionary<string, string> ParseKeyValues(string payload)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string part in payload.Split(new[] { ' ', ';' }, StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = part.IndexOf('=');
            if (eq <= 0 || eq >= part.Length - 1)
                continue;

            string key = part.Substring(0, eq).Trim();
            string val = part.Substring(eq + 1).Trim();
            dict[key] = val;
        }

        return dict;
    }
}
