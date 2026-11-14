using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

public enum PlayerIntentKind
{
    Normal,
    AttackOrKill,
    RiskySkill,
    FalseStateClaim
}

public class ActionAnalysis
{
    public PlayerIntentKind Kind { get; set; }
    public string SanitizedIntent { get; set; }
    public string SuggestedSkill { get; set; } = "athletics";
    public string FalseClaimKind { get; set; } = "";
}

public static class ActionGatekeeper
{
    private static readonly Regex FalseDeathRegex = new(
        @"\b(öldüm|gebedim|geberdim|bayıldım|hayatım\s+bitti|canım\s+(kalmadı|sıfır|bitti)|hp\s*0|kalbim\s+durdu|nefesim\s+durdu|son\s+nefesimi\s+verdim|kendimi\s+öldürdüm|intihar\s+ettim)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled
    );

    private static readonly Regex FalseGodModeRegex = new(
        @"\b(savaşı\s+kazandım|hepsini\s+yendim|iyileştim|canım\s+(doldu|full)|hp\s+full|seviye\s+atladım|zengin\s+oldum|ışınlandım)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled
    );

    private static readonly Regex HallucinatedPlayerDeathRegex = new(
        @"(hayatın\s+sonu|son\s+nefesini|gözlerin\s+karar|bedenini\s+hissedem|cansız\s+beden|\böldün\b|kalbin\s+durdu|ruhunu\s+teslim)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled
    );

    private static readonly Regex AttackRegex = new(
        @"\b(saldır\w*|öldür\w*|kes(?:t[ıi]m|iyorum|erim|eceğim|eyi[mz]|meye)?|kafasını|kafası|boğazını|boğaz|biç(?:t[ıi]m|iyorum|erim|eceğim)?|parçala\w*|doğra\w*|sapla\w*|sok(?:tum|uyorum|arım|acağım)?|vur(?:d[ıiuü]m|uyorum|urum|acağım|ayı[mz]|maya)?|bıçakla\w*|hançerle\w*|ateş\s*et\w*|ok\s*at\w*|yumruk\w*|tekme\w*|savur\w*|kılıc\w*|kılıç\w*|boğ(?:dum|uyorum|arım|acağım)?|ez(?:dim|iyorum|erim|eceğim)?|fırlat\w*)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled
    );

    private static readonly Regex RiskyRegex = new(
        @"\b(kır(?:dım|ıyorum|arım|acağım)?|çal(?:dım|ıyorum|arım|acağım)?|atla\w*|tırman\w*|gizlen\w*|saklan\w*|süzül\w*|ikna\w*|kandır\w*|yalan\s+söyl\w*|gözdağı\w*|korkut\w*|zar\s*at\w*|incele\w*|sezgi\w*|kontrol\s*et\w*|kilit\w*|tuzağ\w*|araştır\w*|dinl(?:iyorum|erim|e))\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled
    );

    private static readonly Regex PlayerThirdPersonRegex = new(
        @"^\s*Oyuncu,?\s+",
        RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.Compiled
    );

    public static string CleanThirdPersonPlayerArtifacts(string narrative)
    {
        if (string.IsNullOrWhiteSpace(narrative))
            return narrative;

        var trCulture = new System.Globalization.CultureInfo("tr-TR");
        string cleaned = PlayerThirdPersonRegex.Replace(narrative, "");

        // Replace English Bandit words with Turkish Haydut
        cleaned = Regex.Replace(cleaned, @"\bBandits\b", "Haydutlar");
        cleaned = Regex.Replace(cleaned, @"\bbandits\b", "haydutlar");
        cleaned = Regex.Replace(cleaned, @"\bBandit\b", "Haydut");
        cleaned = Regex.Replace(cleaned, @"\bbandit\b", "haydut");

        string[] lines = cleaned.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].TrimStart();
            if (line.Length > 0 && char.IsLower(line[0]))
            {
                lines[i] = char.ToUpper(line[0], trCulture) + line.Substring(1);
            }
        }
        return string.Join("\n", lines);
    }

    private static readonly (Regex Pattern, string Replacement)[] GodModSanitizers =
    {
        (new Regex(@"\bkafasını\s+kest\w*\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "kafasına doğru ani bir saldırı yapmaya çalışıyor"),
        (new Regex(@"\b(kestim|keserim|öldürdüm|öldürürüm|biçtim|biçerim|parçaladım|parçalarım|sapladım|saplarım|soktum|sokarım|bıçakladım|hançerledim|boğdum|ezdim|saldırırım|saldırdım)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "saldırı hamlesi yapmaya çalışıyor"),
        (new Regex(@"\b(vurdum|vururum)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "vurmayı deniyor"),
        (new Regex(@"\b(kırdım|kırarım)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "kırmaya çalışıyor"),
        (new Regex(@"\b(çaldım|çalarım)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "gizlice almaya çalışıyor"),
        (new Regex(@"\bikna\s+ettim\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "ikna etmeye çalışıyor"),
        (new Regex(@"\batladım\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "atlamaya çalışıyor")
    };

    public static ActionAnalysis Analyze(string rawInput, int currentHp = 1)
    {
        string trimmed = (rawInput ?? "").Trim();
        var analysis = new ActionAnalysis
        {
            Kind = PlayerIntentKind.Normal,
            SanitizedIntent = trimmed
        };

        if (string.IsNullOrEmpty(trimmed))
            return analysis;

        if (currentHp > 0 && FalseDeathRegex.IsMatch(trimmed))
        {
            analysis.Kind = PlayerIntentKind.FalseStateClaim;
            analysis.FalseClaimKind = "death";
            return analysis;
        }

        if (FalseGodModeRegex.IsMatch(trimmed))
        {
            analysis.Kind = PlayerIntentKind.FalseStateClaim;
            analysis.FalseClaimKind = "godmode";
            return analysis;
        }

        if (AttackRegex.IsMatch(trimmed))
        {
            analysis.Kind = PlayerIntentKind.AttackOrKill;
            analysis.SuggestedSkill = "athletics";
        }
        else if (RiskyRegex.IsMatch(trimmed))
        {
            analysis.Kind = PlayerIntentKind.RiskySkill;
            analysis.SuggestedSkill = GuessSkill(trimmed);
        }

        string sanitized = trimmed;
        foreach (var (pattern, replacement) in GodModSanitizers)
            sanitized = pattern.Replace(sanitized, replacement);

        analysis.SanitizedIntent = sanitized;
        return analysis;
    }

    public static string FramePromptForAi(
        string rawInput,
        ActionAnalysis analysis,
        bool isCombatActive,
        int currentHp = 1,
        int maxHp = 1,
        string currentLocation = "",
        string sceneEntities = "",
        string focusedEntity = "")
    {
        string locInfo = string.IsNullOrWhiteSpace(currentLocation) ? "mevcut konum" : currentLocation;
        string sceneInfo = string.IsNullOrWhiteSpace(sceneEntities) ? "şu anki NPC'ler" : sceneEntities;

        if (analysis != null && analysis.Kind == PlayerIntentKind.FalseStateClaim)
        {
            if (analysis.FalseClaimKind == "death")
            {
                return $"[HAKEM KARARI - GEÇERSİZ ÖLÜM İDDİASI: Oyuncunun gerçek canı {currentHp}/{maxHp} HP; karakter HAYATTA ve KESİNLİKLE ÖLMEDİ! Oyuncu sadece yere atlayıp ölü taklidi yapıyor veya abartıyor. ASLA 'öldün', 'gözlerin karardı', 'son nefesin', 'hayatın sonu' YAZMA! Sadece {locInfo} içindeki mevcut kişilerin ({sceneInfo}) bu tuhaf hareketine şaşkınlıkla verdiği tepkiyi yaz. Önceki mekanlardaki eski NPC'leri ASLA sahneye ışınlama! ASLA [COMBAT] veya [DAMAGE] ekleme.] Oyuncu niyeti: \"{analysis.SanitizedIntent}\"";
            }

            return $"[HAKEM KARARI - GEÇERSİZ DURUM İDDİASI: Oyuncu oyun kuralları/zar olmadan tek taraflı iyileşme, ışınlanma, zafer veya zenginlik ilan edemez (Mevcut HP: {currentHp}/{maxHp}). Bunu gerçek olmuş gibi kabul etme; sadece karakterin bunu dilediğini/iddia ettiğini ve {locInfo} içindeki mevcut durumun değişmediğini anlat.] Oyuncu niyeti: \"{analysis.SanitizedIntent}\"";
        }

        if (isCombatActive)
        {
            return $"[HAKEM KİLİDİ - AKTİF SAVAŞ SÜRÜYOR: Şu an Savaş Paneli aktif! Oyuncunun sohbete yazdığı hiçbir cümle düşmanlara hasar veremez, düşmanları öldüremez veya savaşı bitiremez. ASLA düşman öldü/yere yığıldı/kan aktı yazma! Sadece düşmanların hâlâ ayakta ve tetikte olduğunu söyleyip saldırı için sağdaki Savaş Paneli'ni kullanmasını hatırlat. ASLA [COMBAT: ...] veya [CHECK: ...] ekleme.] Oyuncu mesajı: \"{analysis.SanitizedIntent}\"";
        }

        if (analysis != null && analysis.Kind == PlayerIntentKind.AttackOrKill)
        {
            return $"[HAKEM KARARI: Oyuncu bir SALDIRI GİRİŞİMİ başlattı. Henüz kimse yara almadı, kimse ölmedi! Sadece oyuncunun ani hamlesini, şu an {locInfo} içinde bulunan hedefin geri sıçrayıp kendini savunmasını ve savaşın başlamasını anlat. Önceki mekanlardaki kişileri sahneye katma. ASLA ölüm, kesilmiş baş veya zar sayısı yazma. Cevabının EN SONUNA mutlaka [COMBAT: enemies=...] ekle.] Oyuncu niyeti: \"{analysis.SanitizedIntent}\"";
        }

        if (analysis != null && analysis.Kind == PlayerIntentKind.RiskySkill)
        {
            return $"[HAKEM KARARI: Oyuncu belirsiz/riskli bir eylem DENİYOR. Sonucu kesin kabul etme! Eylemin zorluk anında dur ve cevabının EN SONUNA [CHECK: skill={analysis.SuggestedSkill} dc=13] ekle.] Oyuncu niyeti: \"{analysis.SanitizedIntent}\"";
        }

        return rawInput;
    }

    public static bool EnforcePostRules(
        ActionAnalysis analysis,
        bool isCombatActive,
        AIDMParseResult directives,
        string sceneSnapshot,
        List<string> loadedMonsterIds,
        int currentHp = 1,
        int maxHp = 1)
    {
        if (directives == null)
            return false;

        if (!string.IsNullOrWhiteSpace(directives.CleanNarrative))
        {
            directives.CleanNarrative = CleanThirdPersonPlayerArtifacts(directives.CleanNarrative);
        }

        bool narrativeOverridden = false;

        if (analysis != null && analysis.Kind == PlayerIntentKind.FalseStateClaim)
        {
            directives.CombatEnemies.Clear();
            directives.RollRequest = null;
            directives.DamageAmount = 0;
            directives.HealAmount = 0;

            if (analysis.FalseClaimKind == "death" && currentHp > 0 &&
                HallucinatedPlayerDeathRegex.IsMatch(directives.CleanNarrative ?? ""))
            {
                directives.CleanNarrative = $"Kendini aniden yere bırakıp ölü taklidi yapıyorsun, ancak {currentHp}/{maxHp} HP ile hâlâ sapasağlamsın ve nefesin düzenli şekilde devam ediyor. Yanındakiler bu tuhaf davranışına anlam veremeyip şaşkınlıkla sana bakıyor.";
                narrativeOverridden = true;
            }

            return narrativeOverridden;
        }

        if (isCombatActive)
        {
            directives.CombatEnemies.Clear();
            directives.RollRequest = null;
            return false;
        }

        if (analysis != null && analysis.Kind == PlayerIntentKind.AttackOrKill)
        {
            if (directives.CombatEnemies.Count == 0 && directives.RollRequest == null)
            {
                directives.CombatEnemies = NormalizeEnemyIds(
                    null,
                    $"{directives.SceneUpdate} {directives.CleanNarrative}",
                    loadedMonsterIds,
                    analysis?.SanitizedIntent ?? ""
                );
            }
        }

        if (!isCombatActive && analysis != null && analysis.Kind == PlayerIntentKind.RiskySkill)
        {
            if (directives.RollRequest == null)
            {
                directives.RollRequest = new PendingRollRequest
                {
                    IsSavingThrow = false,
                    TargetName = string.IsNullOrWhiteSpace(analysis.SuggestedSkill) ? "perception" : analysis.SuggestedSkill,
                    DifficultyClass = 13
                };
            }
        }

        if (directives.CombatEnemies.Count > 0)
        {
            directives.CombatEnemies = NormalizeEnemyIds(
                directives.CombatEnemies,
                $"{directives.SceneUpdate} {directives.CleanNarrative}",
                loadedMonsterIds,
                analysis?.SanitizedIntent ?? ""
            );
        }

        if (!string.IsNullOrWhiteSpace(directives.CleanNarrative))
        {
            directives.CleanNarrative = CleanThirdPersonPlayerArtifacts(directives.CleanNarrative);
        }

        return narrativeOverridden;
    }

    public static void EnforcePostRules(
        ActionAnalysis analysis,
        bool isCombatActive,
        StructuredDmResponse dm,
        string sceneSnapshot,
        List<string> loadedMonsterIds)
    {
        if (dm == null)
            return;

        if (!string.IsNullOrWhiteSpace(dm.Anlatim))
        {
            dm.Anlatim = CleanThirdPersonPlayerArtifacts(dm.Anlatim);
        }

        if (isCombatActive)
        {
            // During active combat, chat messages cannot resolve combat or spawn duplicate phantom bandits.
            dm.AksiyonTipi = "none";
            dm.Dusmanlar = new List<string>();
            return;
        }

        if (analysis != null && analysis.Kind == PlayerIntentKind.AttackOrKill)
        {
            if (!string.Equals(dm.AksiyonTipi, "combat", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(dm.AksiyonTipi, "check", StringComparison.OrdinalIgnoreCase))
            {
                dm.AksiyonTipi = "combat";
            }
        }

        if (string.Equals(dm.AksiyonTipi, "combat", StringComparison.OrdinalIgnoreCase))
        {
            dm.Dusmanlar = NormalizeEnemyIds(
                dm.Dusmanlar,
                $"{dm.SahnedeOlanlar} {dm.Anlatim}",
                loadedMonsterIds,
                analysis?.SanitizedIntent ?? ""
            );
        }

        if ((string.Equals(dm.AksiyonTipi, "check", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(dm.AksiyonTipi, "save", StringComparison.OrdinalIgnoreCase)) &&
            dm.ZarDc <= 0)
        {
            dm.ZarDc = 13;
        }
    }

    public static List<string> NormalizeEnemyIds(
        List<string> rawIds,
        string contextText,
        List<string> loadedMonsterIds,
        string playerAttackText = "")
    {
        string attackLower = (playerAttackText ?? "").ToLowerInvariant();
        string contextLower = (contextText ?? "").ToLowerInvariant();

        // 1. ÖNCELİK: Oyuncunun doğrudan kime saldırdığı (playerAttackText)
        if (!string.IsNullOrWhiteSpace(attackLower))
        {
            if (attackLower.Contains("bram") || attackLower.Contains("hancı") || attackLower.Contains("hanci") || attackLower.Contains("barmen") || attackLower.Contains("tezgah"))
            {
                return new List<string> { "npc_bram" };
            }

            if (attackLower.Contains("yabancı") || attackLower.Contains("yabanci") || attackLower.Contains("kukuleta") || attackLower.Contains("eldrin") || attackLower.Contains("korucu"))
            {
                return new List<string> { "npc_eldrin" };
            }

            if (attackLower.Contains("elara") || attackLower.Contains("şifacı") || attackLower.Contains("sifaci"))
            {
                return new List<string> { "npc_elara" };
            }

            if (attackLower.Contains("haydut") || attackLower.Contains("kael") || attackLower.Contains("varo") || attackLower.Contains("zar atan") || attackLower.Contains("masadaki"))
            {
                return new List<string> { "bandit", "bandit" };
            }

            if (attackLower.Contains("hepsine") || attackLower.Contains("herkese"))
            {
                return new List<string> { "bandit", "bandit" };
            }
        }

        // 2. Anlatım/Directives içinde doğrudan NPC hedefi var mı?
        if (contextLower.Contains("hancı bram") || contextLower.Contains("hancıya") || contextLower.Contains("hanciya") || contextLower.Contains("bram'a") || contextLower.Contains("bram'ın"))
        {
            if (!contextLower.Contains("kael") && !contextLower.Contains("varo") && !contextLower.Contains("haydutlar"))
                return new List<string> { "npc_bram" };
        }

        if (contextLower.Contains("eldrin") || contextLower.Contains("korucu eldrin") || contextLower.Contains("kukuletalı yabancı"))
        {
            if (!contextLower.Contains("kael") && !contextLower.Contains("varo") && !contextLower.Contains("haydutlar"))
                return new List<string> { "npc_eldrin" };
        }

        var valid = new HashSet<string>(loadedMonsterIds ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
        var normalized = new List<string>();

        if (rawIds != null)
        {
            foreach (string raw in rawIds)
            {
                string mapped = MapTurkishOrEnglishEnemy(raw, valid);
                if (!string.IsNullOrEmpty(mapped))
                    normalized.Add(mapped);
            }
        }

        if (normalized.Any(id => id.StartsWith("npc_")))
            return normalized;

        if (normalized.Count == 0)
        {
            if (contextLower.Contains("goblin") && valid.Contains("goblin"))
                normalized.Add("goblin");
            if ((contextLower.Contains("ork") || contextLower.Contains("orc")) && valid.Contains("orc"))
                normalized.Add("orc");
            if ((contextLower.Contains("iskelet") || contextLower.Contains("skeleton")) && valid.Contains("skeleton"))
                normalized.Add("skeleton");
            if ((contextLower.Contains("kurt") || contextLower.Contains("wolf")) && valid.Contains("wolf"))
                normalized.Add("wolf");
            if ((contextLower.Contains("haydut") || contextLower.Contains("bandit")) && valid.Contains("bandit"))
                normalized.Add("bandit");

            if (normalized.Count == 0)
            {
                if (valid.Contains("bandit"))
                    normalized.Add("bandit");
                else if (loadedMonsterIds != null && loadedMonsterIds.Count > 0)
                    normalized.Add(loadedMonsterIds[0]);
            }
        }

        if (normalized.Count == 1)
        {
            string singleId = normalized[0];
            if (!singleId.StartsWith("npc_"))
            {
                int inferredCount = InferCountForMonster(singleId, contextLower);
                for (int i = 1; i < inferredCount; i++)
                    normalized.Add(singleId);
            }
        }

        if (normalized.Count == 1 && normalized[0] == "bandit" &&
            (contextLower.Contains("kael") || contextLower.Contains("varo") || contextLower.Contains("zar atan")))
        {
            normalized.Add("bandit");
        }

        return normalized;
    }

    private static int InferCountForMonster(string monsterId, string lowerContext)
    {
        string[] keywords = monsterId switch
        {
            "bandit" => new[] { "haydut", "bandit" },
            "goblin" => new[] { "goblin" },
            "orc" => new[] { "ork", "orc" },
            "skeleton" => new[] { "iskelet", "skeleton" },
            "wolf" => new[] { "kurt", "wolf" },
            _ => Array.Empty<string>()
        };

        foreach (string kw in keywords)
        {
            if (Regex.IsMatch(lowerContext, $@"\b(üç|3)\s+(?:\w+\s+){{0,2}}{kw}"))
                return 3;

            bool hasTwoOrPlural =
                Regex.IsMatch(lowerContext, $@"\b(iki|2)\s+(?:\w+\s+){{0,2}}{kw}") ||
                Regex.IsMatch(lowerContext, $@"\b{kw}(lar|ler)(ın|in|a|e|ı|i|da|de)?\b");

            bool hasSecondMention =
                Regex.IsMatch(lowerContext, $@"\b(bir|ilk)\s+(?:\w+\s+){{0,2}}{kw}") &&
                Regex.IsMatch(lowerContext, $@"\b(diğer|ikinci|öteki|öbür|başka\s+bir|arkasındaki)\s+(?:\w+\s+){{0,2}}{kw}");

            if (hasTwoOrPlural || hasSecondMention)
                return 2;
        }

        return 1;
    }

    private static string MapTurkishOrEnglishEnemy(string raw, HashSet<string> valid)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "";

        string clean = raw.Trim().ToLowerInvariant();
        if (clean == "npc_bram" || clean == "npc_eldrin" || clean == "npc_elara")
            return clean;

        if (valid.Contains(clean))
            return clean;

        if (clean.Contains("bram") || clean.Contains("hancı") || clean.Contains("hanci") || clean.Contains("barmen"))
            return "npc_bram";
        if (clean.Contains("eldrin") || clean.Contains("korucu") || clean.Contains("yabancı") || clean.Contains("yabanci"))
            return "npc_eldrin";
        if (clean.Contains("elara") || clean.Contains("şifacı") || clean.Contains("sifaci"))
            return "npc_elara";

        if (clean.Contains("haydut") || clean.Contains("muhafız") || clean.Contains("adam") || clean.Contains("bandit"))
            return valid.Contains("bandit") ? "bandit" : "";
        if (clean.Contains("goblin"))
            return valid.Contains("goblin") ? "goblin" : "";
        if (clean.Contains("ork") || clean.Contains("orc"))
            return valid.Contains("orc") ? "orc" : "";
        if (clean.Contains("iskelet") || clean.Contains("skeleton"))
            return valid.Contains("skeleton") ? "skeleton" : "";
        if (clean.Contains("kurt") || clean.Contains("wolf"))
            return valid.Contains("wolf") ? "wolf" : "";

        return "";
    }

    private static readonly Regex NamedValleyRoadRegex = new(
        @"\b([A-ZÇĞİÖŞÜ][a-zçğıöşü']+\s+Vadisi)'?(?:ne|na|nde|nda)?\s+giden\s+yol\b",
        RegexOptions.Compiled
    );

    private static readonly Regex LeavingTavernOrIndoorsRegex = new(
        @"\b(taverna(?:nın)?\s+kapısından\s+(?:dışarı\s+)?çık|tavernadan\s+(?:dışarı\s+çık|çık|ayrıl)|han(?:ın\s+kapısından|\s+kapısından|dan)\s+(?:dışarı\s+çık|çık|ayrıl)|köyün\s+dış(?:ında|ına)\s+(?:uzan|çık)|yol\s+boyunca\s+ilerle)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled
    );

    private static readonly (Regex Pattern, string LocationName)[] LocationTransitionPatterns =
    {
        (new Regex(@"\b(eski\s+bir\s+)?kulübe(nin\s+önünde|ye|de)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "Eski Kulübe"),
        (new Regex(@"\b(ormana\s+(gir|dald|koş)|karanlık\s+orman(da|a)|ormanın\s+içinde)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "Orman"),
        (new Regex(@"\b(mağaraya\s+(gir|ilerle)|mağaranın\s+(içinde|önünde))\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "Mağara"),
        (new Regex(@"\b(sokağa\s+(çık|adım)|kasaba\s+meydanı(na|nda)|pazar\s+yeri)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "Kasaba Sokağı"),
        (new Regex(@"\b(zindana\s+(in|gir)|zindanın\s+içinde)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "Zindan"),
        (new Regex(@"\b(kuzey\s+köprüsü|taş\s+köprünün\s+önünde)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "Köprü")
    };

    /// <summary>Infers a scene location change when the AI narrates arriving at a distinct place without emitting [LOCATION].</summary>
    public static string InferLocationTransition(string currentLocation, string narrative)
    {
        if (string.IsNullOrWhiteSpace(narrative))
            return "";

        string cur = currentLocation ?? "";
        foreach (var (pattern, locationName) in LocationTransitionPatterns)
        {
            if (!string.Equals(cur, locationName, StringComparison.OrdinalIgnoreCase) && pattern.IsMatch(narrative))
                return locationName;
        }

        Match valleyMatch = NamedValleyRoadRegex.Match(narrative);
        if (valleyMatch.Success)
        {
            string valleyRoad = $"{valleyMatch.Groups[1].Value.Trim()} Yolu";
            if (!string.Equals(cur, valleyRoad, StringComparison.OrdinalIgnoreCase))
                return valleyRoad;
        }

        if (cur.Contains("Taverna", StringComparison.OrdinalIgnoreCase) &&
            LeavingTavernOrIndoorsRegex.IsMatch(narrative))
        {
            return "Köy Dışı / Yol";
        }

        return "";
    }

    /// <summary>Keeps only entities from the previous scene list whose core name still appears in the transition narrative.</summary>
    public static string PruneStaleSceneEntities(string previousEntities, string narrative)
    {
        if (string.IsNullOrWhiteSpace(previousEntities))
            return "";

        string lowerNarrative = (narrative ?? "").ToLowerInvariant();
        var kept = new List<string>();
        foreach (string rawPart in previousEntities.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            string entry = rawPart.Trim();
            string cleanName = Regex.Replace(entry, @"\s*\([^)]*\)", "").Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(cleanName))
                continue;

            string[] words = cleanName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            string keyToken = words.Length > 0 ? words[^1] : cleanName;
            if (keyToken.Length >= 3 && lowerNarrative.Contains(keyToken))
                kept.Add(entry);
        }

        return string.Join(", ", kept);
    }

    private static string GuessSkill(string text)
    {
        string lower = text.ToLowerInvariant();
        if (lower.Contains("gözdağı") || lower.Contains("korkut"))
            return "intimidation";
        if (lower.Contains("sezgi"))
            return "insight";
        if (lower.Contains("gizlen") || lower.Contains("süzül") || lower.Contains("çal"))
            return "stealth";
        if (lower.Contains("ikna") || lower.Contains("yalan") || lower.Contains("kandır"))
            return lower.Contains("yalan") || lower.Contains("kandır") ? "deception" : "persuasion";
        if (lower.Contains("kilit"))
            return "sleight_of_hand";
        if (lower.Contains("dinle") || lower.Contains("bak"))
            return "perception";
        if (lower.Contains("araştır") || lower.Contains("incele") || lower.Contains("kontrol"))
            return "investigation";
        if (lower.Contains("atla"))
            return "acrobatics";
        return "athletics";
    }
}
