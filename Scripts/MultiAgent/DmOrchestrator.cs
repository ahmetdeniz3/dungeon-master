using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

/// <summary>Central DM agent that decides turn order, dispatches NPC/Player agents, resolves outcomes, and checks incapacitation.</summary>
public class DmOrchestrator
{
    private readonly NpcCombatAgent _npcAgent = new();

    public NpcCombatAgent NpcAgent => _npcAgent;

    /// <summary>Step 1: DM evaluates encounter state and decides whose turn it is and what action applies.</summary>
    public DmTurnDecision DecideTurnStart(CombatEncounter encounter)
    {
        if (encounter == null || !encounter.IsCombatActive)
            return null;

        InitiativeResult current = encounter.GetCurrentCombatant();
        if (current == null)
            return null;

        if (current.IsPlayer)
        {
            DmPlayerCheckpoint cp = EvaluatePlayerCheckpoint(encounter.Player);
            string actionLabel = cp.CanAct ? "oyuncu aksiyonu bekleniyor" : $"aksiyon alınamıyor ({cp.BlockingReason})";
            return new DmTurnDecision
            {
                RoundNumber = encounter.RoundNumber,
                CombatantId = current.CombatantId,
                CombatantName = current.CombatantName,
                IsPlayer = true,
                RecommendedIntent = cp.CanAct ? CombatAgentIntent.Attack : CombatAgentIntent.CannotAct,
                PlannedActionLabel = actionLabel,
                SummaryLine = $"🧭 [DM Karar] Tur {encounter.RoundNumber} — Sıra {current.CombatantName}'de ({actionLabel})."
            };
        }

        CombatMonster monster = encounter.GetMonsterByInstanceId(current.CombatantId);
        CombatAgentIntent intent = _npcAgent.DecideIntent(monster, encounter);
        string planned = intent switch
        {
            CombatAgentIntent.CannotAct => "aksiyon yok (etkisiz)",
            CombatAgentIntent.MoveAndAttack => "yaklaşma + saldırı",
            CombatAgentIntent.HoldAndTaunt => "mevcut konumu koruma",
            _ => "saldırı"
        };

        return new DmTurnDecision
        {
            RoundNumber = encounter.RoundNumber,
            CombatantId = current.CombatantId,
            CombatantName = monster?.DisplayName ?? current.CombatantName,
            IsPlayer = false,
            RecommendedIntent = intent,
            PlannedActionLabel = planned,
            SummaryLine = $"🧭 [DM Karar] Tur {encounter.RoundNumber} — Sıra {monster?.DisplayName ?? current.CombatantName}'de, aksiyon: {planned}."
        };
    }

    /// <summary>Step 4: Checkpoint — determines whether the player agent is conscious and able to act.</summary>
    public DmPlayerCheckpoint EvaluatePlayerCheckpoint(CharacterData player)
    {
        if (player == null)
        {
            return new DmPlayerCheckpoint
            {
                CanAct = false,
                IsUnconscious = true,
                BlockingReason = "karakter verisi yok",
                SummaryLine = "⚖ [DM Kontrol] Karakter bulunamadı -> Sıra atlanıyor."
            };
        }

        if (player.CurrentHP <= 0)
        {
            return new DmPlayerCheckpoint
            {
                CanAct = false,
                IsUnconscious = true,
                BlockingReason = "0 HP / Bilinci kapalı",
                SummaryLine = $"⚖ [DM Kontrol] {player.Name} bilincini kaybetti (0 HP) -> Sıra NPC'ye geçiyor!"
            };
        }

        if (CombatSystem.CannotAct(player.ActiveConditions))
        {
            string reason = FormatBlockingCondition(player.ActiveConditions);
            return new DmPlayerCheckpoint
            {
                CanAct = false,
                IsUnconscious = player.ActiveConditions.Contains("unconscious"),
                BlockingReason = reason,
                SummaryLine = $"⚖ [DM Kontrol] {player.Name} aksiyon alamıyor ({reason}) -> DM hikayeyi devam ettirip sıradaki NPC'ye geçiyor!"
            };
        }

        return new DmPlayerCheckpoint
        {
            CanAct = true,
            IsUnconscious = false,
            BlockingReason = "",
            SummaryLine = $"⚖ [DM Kontrol] {player.Name} aksiyon alabilir ({player.CurrentHP}/{player.MaximumHP} HP) -> Sıra oyuncuya bırakıldı."
        };
    }

    /// <summary>Step 3: DM computes mechanics truth, narrates the clash outcome, and re-evaluates player checkpoint.</summary>
    public async Task<DmResolutionResult> ResolveAndCommentAsync(
        CombatEncounter encounter,
        CombatMonster monster,
        NpcAgentPlayResult npcPlay,
        CombatAction action,
        int prevDistance,
        CancellationToken ct)
    {
        CharacterData player = encounter?.Player;
        string playerName = player?.Name ?? "Oyuncu";
        string enemyName = monster?.DisplayName ?? npcPlay?.AgentName ?? "Düşman";

        string mechanicsLine;
        string outcomeFacts;

        if (action == null || action.AttackResult == null)
        {
            if (encounter != null && encounter.EngagementDistance < prevDistance)
            {
                mechanicsLine = $"👣 {enemyName} yaklaştı (Mesafe: {encounter.EngagementDistance} ft), ancak menzile tam giremedi.";
                outcomeFacts = $"{enemyName} yaklaştı ({encounter.EngagementDistance} ft) ama henüz vuramadı.";
            }
            else
            {
                mechanicsLine = $"{enemyName} bu turda doğrudan hasar veremedi.";
                outcomeFacts = $"{enemyName} saldıramadı (mesafe: {encounter?.EngagementDistance ?? 5} ft).";
            }
        }
        else
        {
            CombatAttackResult r = action.AttackResult;
            if (r.Hit)
            {
                string critTag = r.Critical ? " [KRİTİK!]" : "";
                mechanicsLine = $"{enemyName}, {playerName}'e saldırdı!{critTag} Atış: {r.Attack.DiceRoll} + {r.Attack.Modifier} = {r.Attack.Total} | Hasar: {r.DamageDealt}";
                outcomeFacts = r.TargetDead
                    ? $"{enemyName} saldırdı ve {r.DamageDealt} hasarla {playerName}'i 0 HP'ye düşürdü (bilincini kaybetti)."
                    : $"{enemyName} saldırdı, {r.Attack.Total} ile isabet ettirdi ve {r.DamageDealt} hasar verdi ({playerName} kalan HP: {player?.CurrentHP}/{player?.MaximumHP}).";
            }
            else
            {
                bool hasShield = player != null && !string.IsNullOrEmpty(player.EquippedShieldId);
                string defenseHint = player != null && player.IsDodging
                    ? "savunma/kaçınma manevrası sayesinde"
                    : (hasShield ? "kalkana çarptığı için" : "hedef yana çekildiği için");
                mechanicsLine = $"{enemyName}, {playerName}'e saldırdı ama ıskaladı! Atış: {r.Attack.DiceRoll} + {r.Attack.Modifier} = {r.Attack.Total}";
                outcomeFacts = $"{enemyName} saldırdı ({r.Attack.Total} vs AC {player?.ArmorClass}), ancak {defenseHint} ıskaladı; {playerName} hasar almadı ({player?.CurrentHP}/{player?.MaximumHP} HP).";
            }
        }

        DmPlayerCheckpoint checkpoint = EvaluatePlayerCheckpoint(player);
        string narrative = await GenerateDmCommentaryAsync(enemyName, playerName, npcPlay?.RoleplayLine ?? "", outcomeFacts, ct)
            .ConfigureAwait(false);

        return new DmResolutionResult
        {
            MechanicsLine = mechanicsLine,
            DmNarrative = narrative,
            Checkpoint = checkpoint
        };
    }

    private static async Task<string> GenerateDmCommentaryAsync(
        string enemyName,
        string playerName,
        string npcRoleplay,
        string outcomeFacts,
        CancellationToken ct)
    {
        string sysPrompt =
            "Sen tarafsız D&D 2024 Dungeon Master hakemisin. " +
            "Verilen zar/mekanik sonucunu ASLA değiştirme (isabet/ıska ve hasar kesin gerçektir). " +
            "NPC ajanının hamlesi ile zar sonucunu birleştirip sadece 1-2 vurucu Türkçe cümleyle sonucu anlat (sayı veya etiket yazma).";

        string userPrompt =
            $"NPC Ajanı ({enemyName}): {npcRoleplay}\n" +
            $"Mekanik Sonuç: {outcomeFacts}\n" +
            $"DM yorumunu yaz (2. şahıs veya 3. şahıs kısa anlatım, hedef {playerName}):";

        string ai = await AgentLlmClient.CompleteShortAsync(sysPrompt, userPrompt, maxTokens: 64, temperature: 0.55, ct)
            .ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(ai) &&
            !Regex.IsMatch(ai, @"\b\d+\s*hp\b", RegexOptions.IgnoreCase))
        {
            return ai.Trim();
        }

        return outcomeFacts;
    }

    private static string FormatBlockingCondition(List<string> conditions)
    {
        if (conditions == null || conditions.Count == 0)
            return "aksiyon alamıyor";

        var labels = new List<string>();
        foreach (string c in conditions)
        {
            string mapped = c.Trim().ToLowerInvariant() switch
            {
                "unconscious" => "Bilinçsiz",
                "paralyzed" => "Felçli",
                "stunned" => "Sersemlemiş",
                "petrified" => "Taşlaşmış",
                "incapacitated" => "Etkisiz",
                _ => null
            };
            if (mapped != null)
                labels.Add(mapped);
        }

        return labels.Count > 0 ? string.Join(", ", labels) : "Hareket/Aksiyon Engelli";
    }

    private static readonly (string Keyword, string Label)[] KnownFocusKeywords =
    {
        ("haydut", "İki haydut (kapı yanındaki masada)"),
        ("hancı", "Hancı Bram (tezgâhta)"),
        ("hanci", "Hancı Bram (tezgâhta)"),
        ("barmen", "Hancı Bram (tezgâhta)"),
        ("bram", "Hancı Bram (tezgâhta)"),
        ("yabancı", "Kukuletalı yabancı (arka köşede)"),
        ("kukuletalı", "Kukuletalı yabancı (arka köşede)"),
        ("elara", "Bayan Elara"),
        ("goblin", "Goblin"),
        ("ork", "Ork"),
        ("muhafız", "Muhafız"),
        ("tüccar", "Tüccar")
    };

    /// <summary>Determines which NPC agent the player is currently talking to so unrelated NPCs stay put.</summary>
    public static string ResolveFocusedEntity(string playerInput, string sceneEntities, string previousFocus)
    {
        if (string.IsNullOrWhiteSpace(playerInput))
            return previousFocus ?? "";

        string lower = playerInput.ToLowerInvariant();

        bool hasNpcKeyword = KnownFocusKeywords.Any(k => lower.Contains(k.Keyword));
        bool hasParchment = lower.Contains("parşömen") || lower.Contains("paşömen") || lower.Contains("parsömen") ||
                            lower.Contains("ilan") || lower.Contains("pano") || lower.Contains("kağıt") || lower.Contains("kagit");

        if (hasParchment && !hasNpcKeyword)
            return "";

        if (!string.IsNullOrWhiteSpace(sceneEntities))
        {
            foreach (string rawEntry in sceneEntities.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                string entry = rawEntry.Trim();
                string coreName = Regex.Replace(entry, @"\s*\([^)]*\)", "").Trim().ToLowerInvariant();
                if (coreName.Length < 3)
                    continue;

                // Check core entity words (skip generic adjectives/objects like 'mühürlü', 'parşömen').
                if (coreName.Contains("parşömen") || coreName.Contains("harita") || coreName.Contains("pano"))
                    continue;

                foreach (string token in coreName.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (token.Length >= 4 && lower.Contains(token))
                        return entry;
                }
            }
        }

        foreach (var (kw, label) in KnownFocusKeywords)
        {
            if (lower.Contains(kw))
                return label;
        }

        return previousFocus ?? "";
    }

    /// <summary>Multi-agent orchestration guidance injected into Main.cs system prompt for story/roleplay turns.</summary>
    public static string FormatMultiAgentSystemPromptNote(string currentLocation, string sceneEntities, string focusedEntity = "")
    {
        var sb = new StringBuilder();
        sb.AppendLine("ÇOKLU AJAN (MULTI-AGENT) VE SAHNE SÜREKLİLİĞİ:");
        sb.AppendLine("- Oyuncu (Kahraman Ajanı) kendi kararlarını verir; asla oyuncunun yerine hamle veya konuşma uydurma.");
        sb.AppendLine($"- Aktif Konum: {currentLocation}.");
        if (!string.IsNullOrWhiteSpace(focusedEntity))
            sb.AppendLine($"- Şu Anki Aktif Muhatap (Konuştuğun NPC Ajanı): {focusedEntity}.");
        sb.AppendLine("- YALNIZCA oyuncunun muhatap olduğu veya yanındaki NPC ajanı tepki verir. Uzaktaki diğer NPC'leri (ör. tezgâhtaki hancı, köşedeki yabancı) bir bağlayıcı geçiş olmadan her cevaba zorla sokma!");
        sb.AppendLine("- FİZİKSEL NESNE SÜREKLİLİĞİ: Bir nesne (ör. ilan panosundaki parşömen, yabancının haritası) kimse yürüyüp almadıkça veya açıkça uzatmadıkça bir anda başka birinin eline geçemez! Nesnenin el değiştirmesi gerekiyorsa önce kimin nasıl alıp verdiğini açık bağlayıcı cümleyle yaz.");
        sb.AppendLine("- Sen Yönetici DM Ajanısın: Odağı koru, başarı/başarısızlığı zar veya kurallarla tart, gereksiz kişileri sahneye katma.");
        return sb.ToString().TrimEnd();
    }
}
