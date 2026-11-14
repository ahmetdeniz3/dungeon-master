using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

/// <summary>Autonomous NPC/Monster agent that thinks and roleplays inside its own persona before dice resolution.</summary>
public class NpcCombatAgent
{
    private record NpcPersona(string Tag, string Style, string[] AttackFallbacks, string[] ApproachFallbacks);

    private static readonly Dictionary<string, NpcPersona> Personas = new(StringComparer.OrdinalIgnoreCase)
    {
        ["goblin"] = new(
            "👺",
            "kurnaz, hırçın ve alaycı bir goblin savaşçısı; kısa kesik cümlelerle hırlar",
            new[]
            {
                "Hırlayarak paslı hançerini savurur ve zayıf anını kollar.",
                "Tiz bir çığlıkla diz kapağına doğru sinsi bir hamle yapar.",
                "Sırıtarak paslı kılıcını bacaklarına doğru sallar.",
                "Öfkeyle zıplayıp silahını göğsüne doğru savurur."
            },
            new[]
            {
                "Öfkeyle tıslayıp hızla mesafeyi kapatmaya atılır.",
                "Gözleri parlayarak masaların altından sinsi adımlarla yanaşır.",
                "Çarpık bacaklarıyla beklenmedik bir hızla üstüne doğru fırlar.",
                "Çığlıklar atarak bir sağa bir sola zıplayıp üzerine yaklaşır."
            }
        ),
        ["bandit"] = new(
            "🗡",
            "acımasız, küstah ve fırsatçı bir haydut; tehditkâr ve soğukkanlıdır",
            new[]
            {
                "Dişlerini sıkarak kısa kılıcıyla ani bir yan kesik savurur.",
                "Alaycı bir homurtuyla boşluğunu kollayıp kılıcını ileri saplamaya çalışır.",
                "Omuz vurup dengeni bozmaya çalışarak kılıcını savurur.",
                "Soğukkanlı bir sırıtışla kılıcını alttan yukarı doğru savurur.",
                "Küfrederek bir adım yana kayar ve sert bir darbe indirir."
            },
            new[]
            {
                "Küfrederek kılıcını çeker ve hızla üzerine doğru yürür.",
                "Alaycı bir gülüşle masanın etrafından dolanıp mesafeyi kapatır.",
                "Kılıcını kalkanına vurarak tehditkâr adımlarla sana yaklaşır.",
                "Gözlerini senden ayırmadan çevik adımlarla önünü keser."
            }
        ),
        ["orc"] = new(
            "👹",
            "vahşi ve korkusuz bir ork savaşçısı; kükreyerek güç gösterisi yapar",
            new[]
            {
                "Kulakları sağır eden bir kükremeyle ağır silahını yukarıdan indirir.",
                "Gözü dönmüşçesine baltasını yatay bir kavisle savurur.",
                "Boğuk bir böğürtüyle göğsünü hedef alıp silahını çarpar.",
                "Zırhını parçalamak istercesine acımasız bir darbe indirir."
            },
            new[]
            {
                "Yeri sarsan adımlarla gürleyerek üzerine atılır.",
                "Ağır adımlarla önüne dikilip cüssesiyle yolunu kapatır.",
                "Silahını göğsüne vurarak tehditkâr bir kükremeyle mesafeyi kapatır.",
                "Önüne çıkan engelleri devirip hızla aradaki mesafeyi yok eder."
            }
        ),
        ["skeleton"] = new(
            "💀",
            "sessiz, mekanik ve duygusuz bir ölümsüz iskelet; konuşmaz, kemikleri tıkırdar",
            new[]
            {
                "Çenesi sessizce tıkırdarken paslı kılıcını mekanik bir kesinlikle savurur.",
                "Kemikli parmaklarıyla kılıcını kavrayıp ruhsuzca boynuna sallar.",
                "Göz çukurlarındaki karanlıkla soğuk ve mekanik bir darbe indirir.",
                "Paslı kalkanının ardından kılıcını sessizce ileri saplar."
            },
            new[]
            {
                "Kemikleri tıkırdayarak hedefe doğru sarsak ama kararlı adımlarla ilerler.",
                "Ruhsuz göz çukurlarını dikerek mekanik adımlarla yaklaşır.",
                "Kuru kemik sesleri eşliğinde hiç duraksamadan üzerine yürür.",
                "Sarsak adımlarla ancak acımasız bir inatla mesafeyi kapatır."
            }
        ),
        ["wolf"] = new(
            "🐺",
            "vahşi bir kurt; dişlerini gösterip hırlar ve boğaza atılmaya çalışır",
            new[]
            {
                "Tüylerini kabartıp keskin dişleriyle boğazına atılır.",
                "Alçaktan hırlayarak baldına ani bir ısırık koparmaya çalışır.",
                "Ön pençeleriyle dengeni bozup dişlerini geçirmeyi hedefler.",
                "Öfkeyle uluyup çenesini koluna kenetlemeye çalışır."
            },
            new[]
            {
                "Alçalarak hırlar ve çevik sıçrayışlarla mesafeyi kapatır.",
                "Kuyruğunu kısarak etrafında dairesel hareketlerle döner ve yanaşır.",
                "Hızlı adımlarla çevreni sararak saldırmak için uygun açıyı arar.",
                "Yere sinerek sıçramaya hazır halde sessizce yaklaşır."
            }
        )
    };

    private static readonly NpcPersona DefaultPersona = new(
        "⚔",
        "tehlikeli ve tetikte bir düşman",
        new[]
        {
            "Silahını kararlı bir hamleyle üzerine doğru savurur.",
            "Açığını yakalamaya çalışarak sert bir darbe indirir.",
            "Savunmanı aşmak için ani ve keskin bir saldırı dener.",
            "Gözlerini senden ayırmadan dengeli bir hamle yapar."
        },
        new[]
        {
            "Hızla öne atılarak saldırı pozisyonu alır.",
            "Adımlarını hızlandırıp mesafeyi kapatır.",
            "Silahını kaldırıp üzerine doğru hamle yapar.",
            "Çevik hareketlerle sana doğru yaklaşır."
        }
    );

    private string _lastUsedLine = "";

    public CombatAgentIntent DecideIntent(CombatMonster monster, CombatEncounter encounter)
    {
        if (monster == null || monster.IsDead() || CombatSystem.CannotAct(monster.ActiveConditions))
            return CombatAgentIntent.CannotAct;

        if (monster.Data.Attacks == null || monster.Data.Attacks.Count == 0)
            return CombatAgentIntent.HoldAndTaunt;

        if (encounter != null && encounter.EngagementDistance > 5)
            return CombatAgentIntent.MoveAndAttack;

        return CombatAgentIntent.Attack;
    }

    public async Task<NpcAgentPlayResult> PlayTurnAsync(
        CombatMonster monster,
        CombatEncounter encounter,
        DmTurnDecision dmDecision,
        CancellationToken ct)
    {
        string monsterId = monster?.Data?.Id ?? "";
        NpcPersona persona = Personas.TryGetValue(monsterId, out NpcPersona found) ? found : DefaultPersona;
        CombatAgentIntent intent = dmDecision?.RecommendedIntent ?? DecideIntent(monster, encounter);

        string weaponName = (monster?.Data?.Attacks != null && monster.Data.Attacks.Count > 0)
            ? monster.Data.Attacks[0].Name
            : "silahı";

        string resultLine;
        if (intent == CombatAgentIntent.CannotAct)
        {
            resultLine = "Sersemlemiş durumda sendeliyor ve hamle yapamıyor.";
        }
        else
        {
            string sysPrompt =
                $"Sen şu karakterin ajanısın: {monster.DisplayName} ({persona.Style}). " +
                "Kendi rolüne bürün. Sadece bu turda yapmaya çalıştığın hamleyi ve varsa kısa bir hırıltı/tehdit cümlesini 3. tekil şahısta TEK cümle (en fazla 16 kelime) olarak Türkçe yaz. " +
                "ASLA isabet etti, hasar verdi, öldürdü veya zar sayısı yazma; sonucu DM belirleyecek.";

            string targetName = encounter?.Player?.Name ?? "hedef";
            string moveNote = intent == CombatAgentIntent.MoveAndAttack ? "Önce mesafeyi kapatıp sonra" : "Yakın mesafeden";
            int roundNum = dmDecision?.RoundNumber ?? 1;
            string userPrompt =
                $"Tur {roundNum}. DM kararı: {dmDecision?.PlannedActionLabel ?? "saldırı"}. " +
                $"{moveNote} {targetName}'e {weaponName} ile hamle yapıyorsun (Mevcut HP: {monster.CurrentHP}/{monster.MaximumHP}). " +
                "Önceki turlarla aynı cümleyi kurma, farklı bir betimleme yap. Rolünü oyna:";

            string aiLine = await AgentLlmClient.CompleteShortAsync(sysPrompt, userPrompt, maxTokens: 48, temperature: 0.72, ct)
                .ConfigureAwait(false);

            string[] fallbackPool = intent == CombatAgentIntent.MoveAndAttack ? persona.ApproachFallbacks : persona.AttackFallbacks;
            string fallback = SelectDiverseLine(fallbackPool, roundNum);

            string sanitized = SanitizeNpcRoleplay(aiLine);
            if (string.IsNullOrWhiteSpace(sanitized) || string.Equals(sanitized, _lastUsedLine, StringComparison.OrdinalIgnoreCase))
            {
                resultLine = fallback;
            }
            else
            {
                resultLine = sanitized;
            }
        }

        _lastUsedLine = resultLine;

        return new NpcAgentPlayResult
        {
            AgentName = monster?.DisplayName ?? "Düşman",
            PersonaTag = persona.Tag,
            ChosenIntent = intent,
            RoleplayLine = resultLine
        };
    }

    private string SelectDiverseLine(string[] pool, int roundNumber)
    {
        if (pool == null || pool.Length == 0)
            return "Silahını savurarak saldırıya geçer.";

        if (pool.Length == 1)
            return pool[0];

        // Pick by round offset first
        int preferredIdx = Math.Abs(roundNumber - 1) % pool.Length;
        string picked = pool[preferredIdx];

        if (string.Equals(picked, _lastUsedLine, StringComparison.OrdinalIgnoreCase))
        {
            // Pick next
            picked = pool[(preferredIdx + 1) % pool.Length];
        }

        return picked;
    }

    private static string SanitizeNpcRoleplay(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        string clean = raw.Trim();
        // Reject hallucinated dice/kills/damage if the NPC agent tries to resolve its own outcome.
        if (Regex.IsMatch(clean, @"\b(öldü|yere\s+yığıl|hasar\s+verd|isabet\s+etti|iskaladı|\d+\s*hp)\b", RegexOptions.IgnoreCase))
            return null;

        return clean;
    }
}
