using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;

public class CampaignSavePayload
{
    public string LongTermSummary { get; set; } = "";
    public string CurrentLocation { get; set; } = "Kırık Kalkan Tavernası";
    public string SceneEntities { get; set; } = "";
    public string FocusedEntity { get; set; } = "";
    public List<string> ActiveQuests { get; set; } = new();
    public List<string> CompletedQuests { get; set; } = new();
    public List<string> Facts { get; set; } = new();
    public List<string> RecentEvents { get; set; } = new();
    public List<Dictionary<string, string>> ConversationHistory { get; set; } = new();

    public string PlayerName { get; set; } = "";
    public string PlayerClassId { get; set; } = "";
    public int PlayerCurrentHP { get; set; } = -1;
    public int PlayerMaxHP { get; set; } = -1;
    public int PlayerLevel { get; set; } = 0;
    public int PlayerXP { get; set; } = 0;
    public int PlayerHitDieSides { get; set; } = 0;
    public int PlayerHitDiceRemaining { get; set; } = -1;
    public int PlayerGold { get; set; } = -1;
    public bool PlayerHasShield { get; set; }
    public string PlayerEquippedShieldId { get; set; } = "";
    public Dictionary<string, int> PlayerItems { get; set; }
    public AbilityScores PlayerAbilityScores { get; set; }
    public List<string> PlayerWeapons { get; set; }
    public List<string> PlayerArmors { get; set; }
    public List<string> PlayerKnownSpells { get; set; }
    public Dictionary<int, int> PlayerSpellSlotsMax { get; set; }
    public Dictionary<int, int> PlayerSpellSlotsCurrent { get; set; }
    public string PlayerSpellcastingAbility { get; set; } = "";
    public List<string> PlayerConditions { get; set; } = new();
    public string PlayerEquippedWeaponId { get; set; } = "";
    public string PlayerEquippedArmorId { get; set; } = "";
}

public class CampaignMemory
{
    private const string SaveFilePath = "user://campaign_save.json";
    private const int MaxFacts = 25;
    private const int MaxRecentEvents = 12;

    public string CurrentLocation { get; set; } = "Kırık Kalkan Tavernası";
    public string SceneEntities { get; set; } = "";
    public string FocusedEntity { get; set; } = "";
    public string LongTermSummary { get; set; } = "";
    public List<string> ActiveQuests { get; } = new();
    public List<string> CompletedQuests { get; } = new();
    public List<string> Facts { get; } = new();
    public List<string> RecentEvents { get; } = new();

    public void AddFact(string fact)
    {
        if (string.IsNullOrWhiteSpace(fact))
            return;

        string cleaned = fact.Trim();
        if (Facts.Any(f => string.Equals(f, cleaned, StringComparison.OrdinalIgnoreCase)))
            return;

        Facts.Add(cleaned);
        while (Facts.Count > MaxFacts)
            Facts.RemoveAt(0);
    }

    public void AddEvent(string evt)
    {
        if (string.IsNullOrWhiteSpace(evt))
            return;

        RecentEvents.Add(evt.Trim());
        while (RecentEvents.Count > MaxRecentEvents)
            RecentEvents.RemoveAt(0);
    }

    public void AddQuest(string quest)
    {
        if (string.IsNullOrWhiteSpace(quest))
            return;

        string q = quest.Trim();
        if (!ActiveQuests.Any(x => string.Equals(x, q, StringComparison.OrdinalIgnoreCase)))
            ActiveQuests.Add(q);
    }

    public void CompleteQuest(string quest)
    {
        if (string.IsNullOrWhiteSpace(quest))
            return;

        string q = quest.Trim();
        string match = ActiveQuests.FirstOrDefault(x => x.Contains(q, StringComparison.OrdinalIgnoreCase));
        if (match != null)
            ActiveQuests.Remove(match);

        if (!CompletedQuests.Any(x => string.Equals(x, q, StringComparison.OrdinalIgnoreCase)))
            CompletedQuests.Add(q);
    }

    public void Clear()
    {
        CurrentLocation = "Kırık Kalkan Tavernası";
        SceneEntities = "";
        FocusedEntity = "";
        LongTermSummary = "";
        ActiveQuests.Clear();
        CompletedQuests.Clear();
        Facts.Clear();
        RecentEvents.Clear();
    }

    public string FormatForPrompt()
    {
        var sb = new StringBuilder();
        sb.AppendLine("DÜNYA VE HAFIZA DURUMU (Bunları mutlaka hatırla ve tutarlı kal):");
        sb.AppendLine($"- Mevcut Konum: {CurrentLocation}");

        string sceneSummary = string.IsNullOrWhiteSpace(SceneEntities)
            ? "Şu an yanında bilinen bir NPC veya düşman yok (yalnızsın)."
            : SceneEntities;
        sb.AppendLine($"- ŞU ANKİ SAHNE DÜZENİ ({CurrentLocation}): {sceneSummary}");
        if (!string.IsNullOrWhiteSpace(FocusedEntity))
            sb.AppendLine($"- ŞU ANKİ AKTİF MUHATAP / ODAK: {FocusedEntity}");
        sb.AppendLine("  (ÖNEMLİ: Her NPC ve nesne belirtilen konumunda kalır; biri yürüyüp almadıkça nesne başkasının eline geçmez. Önceki mekanlardan kişi ışınlama! Mekan/kişiler değişirse [LOCATION: ...] ve [SCENE: ...] ekle.)");

        if (ActiveQuests.Count > 0)
            sb.AppendLine($"- Aktif Görevler: {string.Join("; ", ActiveQuests)}");

        if (!string.IsNullOrWhiteSpace(LongTermSummary))
            sb.AppendLine($"- Önceki Özet: {LongTermSummary.Trim()}");

        if (Facts.Count > 0)
        {
            sb.AppendLine("- Alınan Kararlar / Önemli Detaylar:");
            foreach (string fact in Facts)
                sb.AppendLine($"  * {fact}");
        }

        if (RecentEvents.Count > 0)
        {
            sb.AppendLine("- Son Yapılan Hareketler & Zar/Savaş Kayıtları:");
            foreach (string evt in RecentEvents)
                sb.AppendLine($"  * {evt}");
        }

        return sb.ToString();
    }

    public string FormatQuestsForUI()
    {
        if (ActiveQuests.Count == 0 && CompletedQuests.Count == 0)
            return "[color=gray]Henüz aktif görev yok.[/color]";

        var sb = new StringBuilder();
        foreach (string q in ActiveQuests)
            sb.AppendLine($"[color=gold]★ {EscapeBbcode(q)}[/color]");

        foreach (string cq in CompletedQuests.AsEnumerable().Reverse().Take(3))
            sb.AppendLine($"[color=green]✓ {EscapeBbcode(cq)}[/color]");

        return sb.ToString().TrimEnd();
    }

    public string FormatForUI()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"[b]Konum:[/b] {EscapeBbcode(CurrentLocation)}");
        if (!string.IsNullOrWhiteSpace(SceneEntities))
            sb.AppendLine($"[b]Sahnede:[/b] {EscapeBbcode(SceneEntities)}");

        if (!string.IsNullOrWhiteSpace(LongTermSummary))
        {
            sb.AppendLine("");
            sb.AppendLine("[b]Özet:[/b]");
            sb.AppendLine(EscapeBbcode(LongTermSummary.Trim()));
        }

        if (Facts.Count > 0)
        {
            sb.AppendLine("");
            sb.AppendLine("[b]Kararlar & Detaylar:[/b]");
            foreach (string fact in Facts.AsEnumerable().Reverse().Take(8))
                sb.AppendLine($"• {EscapeBbcode(fact)}");
        }

        if (RecentEvents.Count > 0)
        {
            sb.AppendLine("");
            sb.AppendLine("[b]Son Hareketler:[/b]");
            foreach (string evt in RecentEvents.AsEnumerable().Reverse().Take(5))
                sb.AppendLine($"- {EscapeBbcode(evt)}");
        }

        return sb.ToString().TrimEnd();
    }

    public void SaveToDisk(CharacterData player, List<Dictionary<string, string>> history)
    {
        try
        {
            var payload = new CampaignSavePayload
            {
                CurrentLocation = CurrentLocation,
                SceneEntities = SceneEntities,
                FocusedEntity = FocusedEntity,
                LongTermSummary = LongTermSummary,
                ActiveQuests = new List<string>(ActiveQuests),
                CompletedQuests = new List<string>(CompletedQuests),
                Facts = new List<string>(Facts),
                RecentEvents = new List<string>(RecentEvents),
                ConversationHistory = history ?? new List<Dictionary<string, string>>(),
                PlayerName = player?.Name ?? "",
                PlayerClassId = player?.ClassId ?? "",
                PlayerCurrentHP = player?.CurrentHP ?? -1,
                PlayerMaxHP = player?.MaximumHP ?? -1,
                PlayerLevel = player?.Level ?? 0,
                PlayerXP = player?.ExperiencePoints ?? 0,
                PlayerHitDieSides = player?.HitDieSides ?? 10,
                PlayerHitDiceRemaining = player?.HitDiceRemaining ?? -1,
                PlayerGold = player?.Gold ?? 15,
                PlayerHasShield = player?.HasShieldInInventory ?? false,
                PlayerEquippedShieldId = player?.EquippedShieldId ?? "",
                PlayerItems = player != null ? new Dictionary<string, int>(player.Items) : null,
                PlayerAbilityScores = player?.AbilityScores,
                PlayerWeapons = player != null ? new List<string>(player.WeaponInventory) : null,
                PlayerArmors = player != null ? new List<string>(player.ArmorInventory) : null,
                PlayerKnownSpells = player != null ? new List<string>(player.KnownSpells) : null,
                PlayerSpellSlotsMax = player != null ? new Dictionary<int, int>(player.SpellSlotsMax) : null,
                PlayerSpellSlotsCurrent = player != null ? new Dictionary<int, int>(player.SpellSlotsCurrent) : null,
                PlayerSpellcastingAbility = player?.SpellcastingAbility ?? "",
                PlayerConditions = player != null ? new List<string>(player.ActiveConditions) : new List<string>(),
                PlayerEquippedWeaponId = player?.EquippedWeaponId ?? "",
                PlayerEquippedArmorId = player?.EquippedArmorId ?? ""
            };

            string json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
            using var file = Godot.FileAccess.Open(SaveFilePath, Godot.FileAccess.ModeFlags.Write);
            if (file == null)
            {
                GD.PrintErr($"Kayıt dosyası açılamadı: {SaveFilePath}");
                return;
            }

            file.StoreString(json);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"Hafıza kaydetme hatası: {ex.Message}");
        }
    }

    public CampaignSavePayload LoadFromDisk()
    {
        try
        {
            if (!Godot.FileAccess.FileExists(SaveFilePath))
                return null;

            using var file = Godot.FileAccess.Open(SaveFilePath, Godot.FileAccess.ModeFlags.Read);
            if (file == null)
                return null;

            string json = file.GetAsText();
            if (string.IsNullOrWhiteSpace(json))
                return null;

            CampaignSavePayload payload = JsonSerializer.Deserialize<CampaignSavePayload>(json);
            if (payload == null)
                return null;

            CurrentLocation = string.IsNullOrWhiteSpace(payload.CurrentLocation) ? "Kırık Kalkan Tavernası" : payload.CurrentLocation;
            SceneEntities = payload.SceneEntities ?? "";
            FocusedEntity = payload.FocusedEntity ?? "";
            LongTermSummary = payload.LongTermSummary ?? "";

            ActiveQuests.Clear();
            if (payload.ActiveQuests != null)
            {
                foreach (string q in payload.ActiveQuests)
                    AddQuest(q);
            }

            CompletedQuests.Clear();
            if (payload.CompletedQuests != null)
                CompletedQuests.AddRange(payload.CompletedQuests);

            Facts.Clear();
            if (payload.Facts != null)
            {
                foreach (string f in payload.Facts)
                    AddFact(f);
            }

            RecentEvents.Clear();
            if (payload.RecentEvents != null)
            {
                foreach (string e in payload.RecentEvents)
                    AddEvent(e);
            }

            return payload;
        }
        catch (Exception ex)
        {
            GD.PrintErr($"Hafıza yükleme hatası: {ex.Message}");
            return null;
        }
    }

    public void DeleteSaveFile()
    {
        try
        {
            if (Godot.FileAccess.FileExists(SaveFilePath))
            {
                string absPath = ProjectSettings.GlobalizePath(SaveFilePath);
                if (System.IO.File.Exists(absPath))
                    System.IO.File.Delete(absPath);
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"Kayıt dosyası silme hatası: {ex.Message}");
        }
    }

    private static string EscapeBbcode(string text)
    {
        return string.IsNullOrEmpty(text) ? "" : text.Replace("[", "[lb]");
    }
}
