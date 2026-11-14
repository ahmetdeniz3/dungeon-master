using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

public static class CampaignCanon
{
    public const string QuestSilverMine = "Gümüş Maden'deki çeteyi temizle ve kayıp sandığı getir (50 GP)";

    private static readonly HashSet<string> ContinuousActionVerbs = new(StringComparer.OrdinalIgnoreCase)
    {
        "götürüp", "getirip", "gösterip", "uzatıp", "verip", "alıp",
        "gidip", "yaklaşıp", "yürüyüp", "gelip", "dönüp", "varıp",
        "bakıp", "oturup", "kalkıp", "eğilip", "geçip", "durup"
    };

    private static readonly HashSet<string> SpeechVerbs = new(StringComparer.OrdinalIgnoreCase)
    {
        "diyorum", "dedim", "deidm", "derim", "diye",
        "soruyorum", "sordum", "sorarım", "sor",
        "söylüyorum", "söyledim", "söylerim",
        "istiyorum", "istedim", "isterim", "konuşuyorum", "konuştum"
    };

    /// <summary>
    /// Checks whether Kael and Varo (the tavern bandits) have been defeated, knocked out, or killed.
    /// </summary>
    public static bool AreTavernBanditsDead(CampaignMemory memory)
    {
        if (memory == null)
            return false;

        string combined = $"{memory.SceneEntities} {string.Join(" ", memory.Facts)} {string.Join(" ", memory.RecentEvents)}".ToLowerInvariant();
        bool mentionsBandits = combined.Contains("kael") || combined.Contains("varo") || combined.Contains("haydut");
        if (!mentionsBandits)
            return false;

        return combined.Contains("etkisiz") || combined.Contains("öldü") ||
               combined.Contains("yere yığıl") || combined.Contains("savaşı kazandı") ||
               combined.Contains("ölü");
    }

    /// <summary>
    /// Detects the interaction target category based on keywords in player input or entity name.
    /// Tolerates common misspellings (e.g. paşömeni, parsömen, kagidi, ilani).
    /// </summary>
    public static string DetectStepTarget(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "";

        string lower = text.ToLowerInvariant();

        if (lower.Contains("parşömen") || lower.Contains("parşomen") || lower.Contains("paşömen") ||
            lower.Contains("paşomen") || lower.Contains("parsömen") || lower.Contains("parsomen") ||
            lower.Contains("pasomen") || lower.Contains("ilan") || lower.Contains("ilani") ||
            lower.Contains("ilanı") || lower.Contains("pano") || lower.Contains("kağıt") ||
            lower.Contains("kagit") || lower.Contains("kağıdı") || lower.Contains("kagidi") ||
            lower.Contains("mühür"))
            return "parchment";

        if (lower.Contains("haydut") || lower.Contains("kael") || lower.Contains("varo") ||
            lower.Contains("zar atan"))
            return "bandits";

        if (lower.Contains("yabancı") || lower.Contains("kukuleta") || lower.Contains("eldrin") ||
            lower.Contains("harita") || lower.Contains("köşe"))
            return "stranger";

        if (lower.Contains("elara") || lower.Contains("şifacı") || lower.Contains("kulübe"))
            return "elara";

        if (lower.Contains("bram") || lower.Contains("hancı") || lower.Contains("hanci") ||
            lower.Contains("barmen") || lower.Contains("tezgah") || lower.Contains("tezgâh"))
            return "bram";

        if (lower.Contains("herkes") || lower.Contains("içeridekiler") || lower.Contains("ortam"))
            return "all";

        return "";
    }

    /// <summary>
    /// Analyzes the step text to distinguish whether the player is reading the parchment alone,
    /// addressing an NPC alone, or bringing/showing the parchment to an NPC to ask about it.
    /// </summary>
    public static (string PrimaryNpc, bool InvolvesParchment) AnalyzeStepTargets(string stepText, string focusedEntity)
    {
        string textLower = (stepText ?? "").ToLowerInvariant();

        bool involvesParchment = textLower.Contains("parşömen") || textLower.Contains("parşomen") ||
                                 textLower.Contains("paşömen") || textLower.Contains("paşomen") ||
                                 textLower.Contains("parsömen") || textLower.Contains("parsomen") ||
                                 textLower.Contains("pasomen") || textLower.Contains("ilan") ||
                                 textLower.Contains("ilani") || textLower.Contains("ilanı") ||
                                 textLower.Contains("pano") || textLower.Contains("kağıt") ||
                                 textLower.Contains("kagit") || textLower.Contains("kağıdı") ||
                                 textLower.Contains("kagidi") || textLower.Contains("mühür");

        string primaryNpc = "";

        if (textLower.Contains("haydut") || textLower.Contains("kael") || textLower.Contains("varo") || textLower.Contains("zar atan"))
            primaryNpc = "bandits";
        else if (textLower.Contains("yabancı") || textLower.Contains("kukuleta") || textLower.Contains("eldrin") || textLower.Contains("harita") || textLower.Contains("köşe"))
            primaryNpc = "stranger";
        else if (textLower.Contains("elara") || textLower.Contains("şifacı") || textLower.Contains("kulübe"))
            primaryNpc = "elara";
        else if (textLower.Contains("bram") || textLower.Contains("hancı") || textLower.Contains("hanci") || textLower.Contains("barmen") || textLower.Contains("tezgah") || textLower.Contains("tezgâh"))
            primaryNpc = "bram";
        else if (!involvesParchment)
        {
            string focusTarget = DetectStepTarget(focusedEntity);
            primaryNpc = !string.IsNullOrEmpty(focusTarget) && focusTarget != "parchment" && focusTarget != "all"
                ? focusTarget
                : "bram";
        }
        else
        {
            // stepText içinde hiçbir NPC geçmiyor ve InvolvesParchment == true -> PrimaryNpc = "" (sadece parşömen okunuyor)
            primaryNpc = "";
        }

        return (primaryNpc, involvesParchment);
    }

    /// <summary>
    /// Returns the immutable canon lore and world truths for the given location to be injected into the DM system prompt.
    /// </summary>
    public static string GetWorldCanonPrompt(string currentLocation)
    {
        var sb = new StringBuilder();
        sb.AppendLine("DÜNYA VE SENARYO GERÇEKLERİ (CANON LORE - Bu bilgilere ASLA ters düşme):");
        sb.AppendLine("- ANA GÖREV & PARŞÖMEN: İlan panosundaki kırmızı mühürlü parşömen Kasaba Muhafız Yüzbaşısı tarafından asılmıştır.");
        sb.AppendLine("  Üzerinde KESİN OLARAK şu yazar: \"Kırlangıç Vadisi'ndeki Terk Edilmiş Gümüş Maden'i işgal eden goblin ve haydut çetesini temizleyip kayıp tüccar sandığını getirene 50 Altın (GP) ödül verilecektir.\"");
        sb.AppendLine("  (KESİN YASAK: Asla kayıp köpek, tazı, Lord Valerius veya başka bir görev uydurma!)");
        sb.AppendLine("- KİLİT NPC'LER:");
        sb.AppendLine("  * Hancı Bram: Geniş omuzlu, kır sakallı, eski kalkan dövmeli emekli muhafız çavuşu. Bakır kupa parlatırken tavernayı süzer, babacan konuşur. Madendeki çeteden ve Bayan Elara'dan bahseder.");
        sb.AppendLine("  * İki Haydut (Kael ve Varo): Kapı yanındaki masada zar atan kaba adamlar. Çizmelerinde Gümüş Maden'in kızıl kil çamuru vardır. Sıkıştırılırlarsa dövüşür, ikna/gözdağıyla (DC 13) nöbetçi parolası 'Kızıl Kuzgun'u söylerler.");
        sb.AppendLine("  * Kukuletalı Yabancı (Korucu Eldrin): Yağmurdan ıslanmış koyu yeşil pelerinli, yanağında pençe izi olan, önünde işaretli vadi haritası duran tecrübeli sınır korucusu. Çökmüş havalandırma bacasını, goblin tuzaklarını ve kayıp sandığı bilir.");
        sb.AppendLine("  * Bayan Elara (Eski Kulübe): Kasaba çıkışındaki adaçayı kokulu ahşap kulübede bilge şifacı. İksirler, panzehirler ve şifalı otlar satar, Gölge Dikeni zehrini bilir.");
        sb.AppendLine("- BÖLGE VE MEKAN BAĞLANTILARI:");
        sb.AppendLine("  * Kırık Kalkan Tavernası <-> Kasaba Sokağı <-> Eski Kulübe <-> Kırlangıç Vadisi Yolu <-> Terk Edilmiş Gümüş Maden.");
        sb.AppendLine();
        sb.AppendLine("KESİN DİREKTİFLER:");
        sb.AppendLine("- ANLATIM DİLİ (KESİN): Asla 'Oyuncu yürüyor', 'Oyuncu soruyor' gibi 3. tekil şahıs KULLANMA! Doğrudan oyuncuya hitaben 2. tekil şahıs ('Masaya yaklaşıyorsun...', 'Yabancıya soruyorsun...') kullan.");
        sb.AppendLine("- TEKRAR YASAĞI: Önceki mesajlarda kullandığın cümleleri veya NPC repliklerini ASLA kelimesi kelimesine tekrarlama! Her turda karakterin farklı bir jestini, ortam detayını veya aşağıdaki farklı bir bilgi maddesini kullan.");

        if (!string.IsNullOrWhiteSpace(currentLocation))
            sb.AppendLine($"- OYUNCUNUN ŞU ANKİ BULUNDUĞU MEKAN: {currentLocation}");

        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// Returns the scoped canon lore and single focus card for the current action step.
    /// Supports the combination where player brings/shows the parchment to an NPC and asks for details.
    /// </summary>
    public static string GetScopedCanonPrompt(string stepText, string currentLocation, string focusedEntity, CampaignMemory memory = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine("DÜNYA VE SENARYO GERÇEKLERİ (CANON LORE - Bu bilgilere ASLA ters düşme):");
        if (!string.IsNullOrWhiteSpace(currentLocation))
            sb.AppendLine($"- OYUNCUNUN ŞU ANKİ BULUNDUĞU MEKAN: {currentLocation}");
        sb.AppendLine("- BÖLGE VE MEKAN BAĞLANTILARI: Kırık Kalkan Tavernası <-> Kasaba Sokağı <-> Eski Kulübe <-> Kırlangıç Vadisi Yolu <-> Terk Edilmiş Gümüş Maden.");
        sb.AppendLine();
        sb.AppendLine("KESİN DİREKTİFLER:");
        sb.AppendLine("- ANLATIM DİLİ (KESİN): Asla 'Oyuncu yürüyor', 'Oyuncu soruyor' gibi 3. tekil şahıs KULLANMA! Doğrudan oyuncuya hitaben 2. tekil şahıs ('Masaya yaklaşıyorsun...', 'Yabancıya soruyorsun...') kullan.");
        sb.AppendLine("- TEKRAR YASAĞI: Önceki mesajlarda kullandığın cümleleri veya NPC repliklerini ASLA kelimesi kelimesine tekrarlama! Her turda karakterin farklı bir jestini, ortam detayını veya aşağıdaki farklı bir bilgi maddesini kullan.");
        sb.AppendLine();

        var (primaryNpc, involvesParchment) = AnalyzeStepTargets(stepText, focusedEntity);

        // Durum A: Sadece parşömen okunuyor/alınıyor
        if (involvesParchment && string.IsNullOrEmpty(primaryNpc))
        {
            sb.AppendLine("BU ADIMIN TEK ODAĞI (KIRMIZI MÜHÜRLÜ PARŞÖMEN):");
            sb.AppendLine("- İlan panosundaki kırmızı mühürlü parşömen Kasaba Muhafız Yüzbaşısı tarafından asılmıştır.");
            sb.AppendLine("  Üzerinde KESİN OLARAK şu yazar: \"Kırlangıç Vadisi'ndeki Terk Edilmiş Gümüş Maden'i işgal eden goblin ve haydut çetesini temizleyip kayıp tüccar sandığını getirene 50 Altın (GP) ödül verilecektir.\"");
            sb.AppendLine("  (KESİN YASAK: Asla kayıp köpek, tazı veya başka bir görev uydurma!)");
            sb.AppendLine("- GÖREVİN: Sadece oyuncunun parşömene uzanmasını/almasını ve üzerindeki bu metni okumasını anlat. Bu adımda Hancı Bram'ı, haydutları veya yabancıyı ASLA konuşturma!");
            return sb.ToString().TrimEnd();
        }

        // NPC kartını ekle
        string npcTitle;
        switch (primaryNpc)
        {
            case "bandits":
                npcTitle = "Haydutlar (Kael ve Varo)";
                if (AreTavernBanditsDead(memory))
                {
                    sb.AppendLine("BU ADIMIN TEK ODAĞI (İKİ HAYDUT - KAEL VE VARO):");
                    sb.AppendLine("- Kael ve Varo az önceki kavgada yenilip yere yığıldılar, baygın/ölü haldeler ve konuşamazlar! Sadece yerde hareketsiz yattıklarını anlat, onları ASLA konuşturma!");
                }
                else
                {
                    sb.AppendLine("BU ADIMIN TEK ODAĞI (İKİ HAYDUT - KAEL VE VARO):");
                    sb.AppendLine("- Görünüm & Atmosfer: Kael iri yarı, kırık burunlu, kemerinde çentikli bir kısa kılıç taşıyor; Varo ise zayıf, sinsi bakışlı, parmakları arasında kemik zarlar çeviriyor. Çizmelerinde Gümüş Maden'in kızıl çamuru var. Kaba, alaycı ve sokak ağzıyla konuşurlar.");
                    sb.AppendLine("- Sahip Olduğu Bilgiler (Her turda farklı bir detayı kullan):");
                    sb.AppendLine("  1. İlk yaklaşımda oyuncuyu küçümseyip masadaki zar oyununa veya bahse çekmeye çalışırlar.");
                    sb.AppendLine("  2. Çizmelerindeki kızıl çamur veya maden sorulursa tedirgin olup sertleşirler.");
                    sb.AppendLine("  3. İkna veya gözdağı (DC 13) başarılı olursa maden girişindeki nöbetçi parolasının \"Kızıl Kuzgun\" olduğunu ve içeride goblinlerle ortak çalıştıklarını ağzından kaçırırlar.");
                    sb.AppendLine("- GÖREVİN: Bu adımda SADECE haydutları konuştur; Bram veya yabancıyı karıştırma.");
                }
                break;

            case "stranger":
                npcTitle = "Kukuletalı Yabancı (Korucu Eldrin)";
                sb.AppendLine("BU ADIMIN TEK ODAĞI (KUKULETALI YABANCI - KORUCU ELDRIN):");
                sb.AppendLine("- Görünüm & Atmosfer: Yağmurdan nemlenmiş koyu yeşil pelerinli, sol yanağında eski bir pençe izi olan, önünde kömür kalemiyle işaretlenmiş deri bir vadi haritası duran tecrübeli sınır korucusu. Kısık, gizemli ve ölçülü bir sesle konuşur.");
                sb.AppendLine("- Sahip Olduğu Bilgiler (Her soruda/turda farklı bir detayı kullan, aynı cümleyi tekrarlama):");
                sb.AppendLine("  1. Haritadaki İşaretler: Gümüş Maden'in ana girişine kırmızı çarpı koymuştur; kuzeybatıdaki \"Çökmüş Havalandırma Bacası\" ise çeteye görünmeden sızmak için gizli bir yan giriş olarak işaretlenmiştir.");
                sb.AppendLine("  2. Kızıl Diş Goblinleri ve Tuzaklar: Madenin ilk tünellerine ses çıkaran kemik çıngıraklar ve zehirli ok tuzakları kurulduğunu, meşaleyle dikkatsizce girilmemesi gerektiğini bilir.");
                sb.AppendLine("  3. Tavernadaki Haydutlar: Kapı yanındaki Kael ve Varo'nun çizmelerindeki kızıl kil çamurunun sadece Gümüş Maden çevresinde bulunduğunu fark etmiştir; onların çetenin kasabadaki gözcüleri olduğundan şüphelenir.");
                sb.AppendLine("  4. Kayıp Sandık: Çalınan tüccar sandığının içinde lonca mühürlü gümüş külçeler ve eski bir maden anahtarı olduğunu duymuştur.");
                sb.AppendLine("- GÖREVİN: Bu adımda SADECE kukuletalı yabancıyı konuştur; diğer NPC'leri karıştırma.");
                break;

            case "elara":
                npcTitle = "Bayan Elara";
                sb.AppendLine("BU ADIMIN TEK ODAĞI (BAYAN ELARA):");
                sb.AppendLine("- Görünüm & Atmosfer: Kurutulmuş adaçayı, lavanta ve simya buharı kokan ahşap kulübedeki gümüş saçlı bilge şifacı. Sakin, şefkatli ama uyarıcı bir dille konuşur.");
                sb.AppendLine("- Sahip Olduğu Bilgiler: Madendeki goblinlerin oklarına sürdüğü \"Gölge Dikeni\" zehrini tanır, şifa iksiri ve panzehir konusunda yardımcı olur, vadideki zehirli mantarlara karşı uyarır.");
                sb.AppendLine("- GÖREVİN: Bu adımda SADECE Bayan Elara ile olan etkileşimi anlat.");
                break;

            default: // bram
                npcTitle = "Hancı Bram";
                sb.AppendLine("BU ADIMIN TEK ODAĞI (HANCI BRAM):");
                sb.AppendLine("- Görünüm & Atmosfer: Geniş omuzlu, kır sakallı, sağ kolunda eski bir kalkan dövmesi olan emekli muhafız çavuşu. Bakır kupaları bezle parlatırken tavernayı süzer; babacan ve boğuk bir sesle (\"yolcu\", \"evlat\") konuşur.");
                sb.AppendLine("- Sahip Olduğu Bilgiler (Her turda farklı bir detayı kullan):");
                sb.AppendLine("  1. Muhafız Yüzbaşısı'nın astığı 50 altınlık ödül ilanından ve madene giden son iki avcının geri dönmediğinden bahseder.");
                sb.AppendLine("  2. Ocaktaki sıcak arpa yahnisini ve fıçı birasını över, yağmurlu gecede vadiye tok karnına çıkılmasını öğütler.");
                sb.AppendLine("  3. Kasaba çıkışındaki Eski Kulübe'de yaşayan şifacı Bayan Elara'nın goblin zehirlerine karşı panzehir ve şifa iksiri yaptığını anlatır.");
                sb.AppendLine("- GÖREVİN: Bu adımda SADECE Hancı Bram 1-2 cümleyle karşılık versin; masadaki haydutları veya köşedeki yabancıyı ASLA konuşturma!");
                break;
        }

        // Durum B: Oyuncu parşömeni bir NPC'ye götürüp/gösterip soruyor!
        if (involvesParchment && !string.IsNullOrEmpty(primaryNpc))
        {
            sb.AppendLine();
            sb.AppendLine("OYUNCUNUN GÖSTERDİĞİ / SORDUĞU PARŞÖMENİN DETAYLARI (NPC BU KONUDA CEVAP VERMELİ!):");
            sb.AppendLine("- Parşömen Kasaba Muhafız Yüzbaşısı'nın astığı 50 Altın (GP) ödüllü resmi ilandır: Kırlangıç Vadisi'ndeki Terk Edilmiş Gümüş Maden'i işgal eden Kızıl Diş goblinleri ve haydut çetesini temizleyip kayıp lonca sandığını getirme görevidir.");
            sb.AppendLine($"- GÖREVİN: Oyuncu parşömeni {npcTitle}'na götürüp bunun hakkında detay istedi! {npcTitle}'nın bira/arazi gibi alakasız konulara sapmadan DOĞRUDAN bu parşömendeki Gümüş Maden görevi, kayıp sandık ve madendeki tehlikeler hakkında bildiği özel detayları anlatmasını sağla!");
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// Builds a targeted 10-14 line micro-system prompt based on the specific action pipeline module.
    /// Prevents 4B/12B models from hallucinating technical tags, complex branches, or speaking for dead NPCs.
    /// </summary>
    public static string BuildMicroStepSystemPrompt(
        string stepText,
        ActionAnalysis analysis,
        CampaignMemory memory,
        CharacterData player,
        bool isCombatActive)
    {
        var sb = new StringBuilder();

        // Ortak Çekirdek
        sb.AppendLine("Sen karanlık ortaçağ fantezi dünyasında geçen bir hikayenin Dungeon Master'ısın. SADECE akıcı, doğal ve edebi Türkçe kullan.");
        sb.AppendLine("KESİN KURAL 1 (GEÇMİŞ LOG TUTARLILIĞI): Aşağıdaki 'KAYITLI MACERA LOGU' bölümündeki önceki konuşmaları ve olayları DİKKATLE OKU! Bir önceki mesajda kim ne dediyse, kimin elinde hangi eşya/silah varsa ve oyuncu kiminle konuşuyorsa ONA GÖRE mantıklı, sebep-sonuç ilişkili bir cevap yaz. Bir tur önce elinde kupa/kaşık olan birinin bir sonraki turda durup dururken elinde kılıç olduğunu YAZMA!");
        sb.AppendLine("KESİN KURAL 2 (SAÇMA VE ABARTILI CÜMLE YASAĞI): 'Metalin havayı yırtan sesi tavanda yankılanıyor', 'tahta kaşığı kalkan gibi öne sürüyor', 'yoğun buharı havaya savuruyor' gibi saçma, yapay ve abartılı edebiyat CÜMLELERİ ASLA KURMA! Sade, doğal, gerçekçi ve anlaşılır bir Türkçe kullan (2-3 cümle).");
        sb.AppendLine("KESİN KURAL 3 (DOĞRUDAN YANIT): Oyuncu ne sorduysa veya ne yaptıysa doğrudan ona karşılık ver. Oyuncu 'siz ne alaka', 'neden öyle dedin' gibi bir tepki verirse logdaki son olaya bakarak mantıklı bir açıklama/tepki ver.");
        sb.AppendLine("KESİN KURAL 4 (ANLATIM ŞEKLİ): 2. tekil şahıs ('yaklaşıyorsun', 'görüyorsun') ve şimdiki zaman ('-yor') kullan. Asla 'Oyuncu şöyle yapıyor' deme. Oyuncunun yerine karar verme ve zar sonucu uydurma. Asla köşeli parantezli teknik etiket veya seçenek listesi yazma.");
        sb.AppendLine();

        string s = stepText ?? "";

        // Modül 1 — Savaş Sonu Özeti
        if (s.StartsWith("[SİSTEM SAVAŞ SONUCU]", StringComparison.OrdinalIgnoreCase) ||
            s.Contains("[SİSTEM SAVAŞ", StringComparison.OrdinalIgnoreCase))
        {
            sb.AppendLine("GÖREV PARÇASI (SAVAŞ SONU BETİMLEMESİ): Savaş az önce bitti! Yere yığılan düşmanların artık yenildiğini ve hareketsiz kaldığını, oyuncunun nefes nefese kılıcını indirdiğini ve etraftaki sağ kalanların şaşkın/temkinli bakışlarını 2-3 cümleyle anlat. KESİN YASAK: Yenilen düşmanları ASLA ayağa kaldırıp tekrar tehdit ettirme veya konuşturma! 'Bandit' kelimesini kullanma, Türkçe isimlerini kullan.");
        }
        else if (s.StartsWith("[ZAR SONUCU]", StringComparison.OrdinalIgnoreCase) ||
                 s.StartsWith("[SİSTEM ZAR", StringComparison.OrdinalIgnoreCase))
        {
            // Modül 2 — Zar Sonucu Çözümlemesi
            sb.AppendLine("GÖREV PARÇASI (ZAR SONUCU ANLATIMI): Oyuncu az önce zar attı. Zar BAŞARILI ise eylemin/iknanın olumlu sonucunu ve elde edilen yeni ipucunu anlat; BAŞARISIZ ise engeli veya NPC'nin ters tepkisini anlat.");
            sb.AppendLine();
            sb.AppendLine(GetScopedCanonPrompt(stepText, memory?.CurrentLocation, memory?.FocusedEntity, memory));
        }
        else if (analysis != null && analysis.Kind == PlayerIntentKind.AttackOrKill)
        {
            // Modül 3 — Saldırı Başlangıcı
            var (primaryNpc, _) = AnalyzeStepTargets(s, memory?.FocusedEntity);
            string targetName = primaryNpc switch
            {
                "bram" => "Hancı Bram",
                "stranger" => "Korucu Eldrin",
                "elara" => "Bayan Elara",
                _ => "Kael ve Varo (Haydutlar)"
            };

            sb.AppendLine($"GÖREV PARÇASI (SAVAŞ BAŞLANGICI): Oyuncu ani bir saldırı hamlesi başlattı! Henüz kimse ölmedi veya yaralanmadı. Sadece oyuncunun silahına davranmasını ve hedefin ({targetName}'ın savunma veya karşılık pozisyonuna geçmesini) gerilimli ve gerçekçi bir dille 2-3 cümlede anlat. Hedef dışındaki alakasız NPC'leri olaya karıştırma!");
        }
        else if (analysis != null && analysis.Kind == PlayerIntentKind.RiskySkill)
        {
            // Modül 4 — Riskli Eylem / Beceri Girişimi
            sb.AppendLine("GÖREV PARÇASI (RİSKLİ GİRİŞİM): Oyuncu sonucu belirsiz bir eylem deniyor. Sonucu henüz gerçekleşmiş gibi yazma; sadece oyuncunun girişimini ve ortamın gerilimini 2 cümlede anlatıp zar atışı için sahneyi askıda bırak.");
            sb.AppendLine();
            sb.AppendLine(GetScopedCanonPrompt(stepText, memory?.CurrentLocation, memory?.FocusedEntity, memory));
        }
        else
        {
            // Modül 5 — Odaklı Diyalog ve Keşif (Varsayılan Normal Adım)
            sb.AppendLine(GetScopedCanonPrompt(stepText, memory?.CurrentLocation, memory?.FocusedEntity, memory));
        }

        if (player != null)
        {
            string weapon = DataManager.Instance?.GetWeaponData(player.EquippedWeaponId)?.Name ?? player.EquippedWeaponId ?? "Kılıç";
            sb.AppendLine();
            sb.AppendLine($"DURUM: {player.Name} ({player.ClassId}), HP: {player.CurrentHP}/{player.MaximumHP}, Silah: {weapon}, Mekan: {memory?.CurrentLocation ?? "Taverna"}");
        }

        if (memory != null && memory.RecentEvents.Count > 0)
        {
            var recent = memory.RecentEvents.TakeLast(2);
            sb.AppendLine($"SON OLAYLAR: {string.Join(" | ", recent)}");
        }

        string logContent = AdventureLogger.ReadRecentLogForPrompt(14);
        if (!string.IsNullOrWhiteSpace(logContent))
        {
            sb.AppendLine();
            sb.AppendLine("=== KAYITLI MACERA LOGU (ÖNCEKİ KONUŞMALAR VE OLAYLAR - BUNA SADIK KAL) ===");
            sb.AppendLine(logContent);
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// Decomposes compound player input into up to 3 sequential distinct steps.
    /// Does not decompose attacks, false state claims, or actions combined with speech/pronoun references.
    /// </summary>
    public static List<string> DecomposePlayerActions(string rawInput)
    {
        if (string.IsNullOrWhiteSpace(rawInput))
            return new List<string>();

        string trimmed = rawInput.Trim();

        // 1. Saldırı veya FalseStateClaim ise bölmeden tek eleman döndür
        var analysis = ActionGatekeeper.Analyze(trimmed);
        if (analysis != null && (analysis.Kind == PlayerIntentKind.AttackOrKill || analysis.Kind == PlayerIntentKind.FalseStateClaim))
        {
            return new List<string> { trimmed };
        }

        var result = new List<string>();

        // 2. diyip / deyip kalıbı: ör. "selamın aleyküm diyip parşömeni okumaya başlarım"
        var diyipMatch = Regex.Match(trimmed, @"^(.*?)(?:,\s*)?(?:\b(?:diyip|deyip)\b)\s+(.+)$", RegexOptions.IgnoreCase);
        if (diyipMatch.Success)
        {
            string speech = diyipMatch.Groups[1].Value.Trim().Trim('"', '\'');
            string rest = diyipMatch.Groups[2].Value.Trim();

            string targetSpeech = DetectStepTarget(speech);
            string targetRest = DetectStepTarget(rest);

            // İkinci kısım farklı bir nesne/eylem içeriyorsa böl
            string restLower = rest.ToLowerInvariant();
            bool hasDifferentObjectOrAction = restLower.Contains("parşömen") || restLower.Contains("paşömen") ||
                                              restLower.Contains("parsömen") || restLower.Contains("ilan") ||
                                              restLower.Contains("pano") || restLower.Contains("kağıt") ||
                                              restLower.Contains("mühür") || restLower.Contains("kapı") ||
                                              restLower.Contains("dışarı") || restLower.Contains("saldır") ||
                                              restLower.Contains("kılıç") ||
                                              (!string.IsNullOrEmpty(targetSpeech) && !string.IsNullOrEmpty(targetRest) && targetSpeech != targetRest);

            if (hasDifferentObjectOrAction)
            {
                string firstAction;
                if (speech.Contains("selam", StringComparison.OrdinalIgnoreCase) || speech.Contains("merhaba", StringComparison.OrdinalIgnoreCase))
                {
                    firstAction = $"İçeridekilere '{speech}' diyerek selam veriyorum";
                }
                else
                {
                    firstAction = $"'{speech}' diyorum";
                }

                result.Add(firstAction);
                var subActions = DecomposePlayerActions(rest);
                if (subActions.Count > 0)
                    result.AddRange(subActions);
                else
                    result.Add(rest);

                return CapActions(result, 3);
            }
        }

        // 3. Bağlaçlar: (sonra da, daha sonra, ardından, peşinden, sonra)
        var conjMatch = Regex.Match(trimmed, @"^(.*?)(?:,\s*|\s+)(?:ve\s+)?\b(sonra\s+da|daha\s+sonra|ardından|peşinden|sonra)\b\s+(.+)$", RegexOptions.IgnoreCase);
        if (conjMatch.Success)
        {
            string p1 = conjMatch.Groups[1].Value.Trim().TrimEnd(',', '.');
            string p2 = conjMatch.Groups[3].Value.Trim().TrimStart(',', '.');

            if (!string.IsNullOrWhiteSpace(p1) && !string.IsNullOrWhiteSpace(p2))
            {
                var p1List = DecomposePlayerActions(p1);
                result.AddRange(p1List.Count > 0 ? p1List : new List<string> { p1 });

                var p2List = DecomposePlayerActions(p2);
                result.AddRange(p2List.Count > 0 ? p2List : new List<string> { p2 });

                return CapActions(result, 3);
            }
        }

        // 4. Zarf-fiil (-ıp/-ip/-up/-üp) + devam cümlesi: Regex @"^(.+?\b\w+(?:ıp|ip|up|üp))\s+(.+)$"
        var zarfMatch = Regex.Match(trimmed, @"^(.+?\b\w+(?:ıp|ip|up|üp))\s+(.+)$", RegexOptions.IgnoreCase);
        if (zarfMatch.Success)
        {
            string left = zarfMatch.Groups[1].Value.Trim().TrimEnd(',', '.');
            string right = zarfMatch.Groups[2].Value.Trim().TrimStart(',', '.');

            string[] leftWords = left.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            string lastLeftWord = leftWords.Length > 0 ? leftWords[^1].TrimEnd(',', '.', '!', '?').ToLowerInvariant() : "";

            string[] rightWords = right.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            string lastRightWord = rightWords.Length > 0 ? rightWords[^1].TrimEnd(',', '.', '!', '?').ToLowerInvariant() : "";
            string rightLower = right.ToLowerInvariant();

            bool isGeneralGreeting = left.Contains("herkesi selamla", StringComparison.OrdinalIgnoreCase) ||
                                     left.Contains("selam verip", StringComparison.OrdinalIgnoreCase) ||
                                     left.Contains("selamlayıp", StringComparison.OrdinalIgnoreCase);

            bool isContinuousVerb = ContinuousActionVerbs.Contains(lastLeftWord);

            bool endsWithSpeechVerb = SpeechVerbs.Contains(lastRightWord) ||
                                      lastRightWord.StartsWith("sor") ||
                                      lastRightWord.StartsWith("de") ||
                                      lastRightWord.StartsWith("söyle") ||
                                      lastRightWord.StartsWith("iste");

            bool hasAnaphoricReference = rightLower.Contains("bunun") ||
                                         rightLower.Contains("onun") ||
                                         rightLower.Contains("şunun") ||
                                         rightLower.Contains("bu kağıt") ||
                                         rightLower.Contains("bu parşömen") ||
                                         rightLower.Contains("bu paşömen") ||
                                         rightLower.Contains("hakkında");

            string targetLeft = DetectStepTarget(left);
            string targetRight = DetectStepTarget(right);
            bool hasDifferentPhysicalTargets = !string.IsNullOrEmpty(targetLeft) &&
                                               !string.IsNullOrEmpty(targetRight) &&
                                               targetLeft != targetRight;

            // KORUMA: Nesne götürüp/uzatıp sorma veya zamirle bağlı konuşma tek bir sahne eylemidir, asla bölünmez!
            if (!isGeneralGreeting && (isContinuousVerb || endsWithSpeechVerb || hasAnaphoricReference))
            {
                if (endsWithSpeechVerb || hasAnaphoricReference || !hasDifferentPhysicalTargets)
                {
                    return new List<string> { trimmed };
                }
            }

            int rightWordsCount = rightWords.Length;
            if (leftWords.Length >= 2 || rightWordsCount >= 2 || left.EndsWith("verip", StringComparison.OrdinalIgnoreCase) || left.EndsWith("bakıp", StringComparison.OrdinalIgnoreCase))
            {
                string conjugatedLeft = ConjugateZarfFiil(left);
                result.Add(conjugatedLeft);

                var rightList = DecomposePlayerActions(right);
                result.AddRange(rightList.Count > 0 ? rightList : new List<string> { right });

                return CapActions(result, 3);
            }
        }

        // 5. Virgül veya " ve " ile ayrılmış iki ayrı fiilli cümle
        var veMatch = Regex.Match(trimmed, @"^(.*?)(?:,\s*|\s+)ve\s+(.+)$", RegexOptions.IgnoreCase);
        if (veMatch.Success)
        {
            string left = veMatch.Groups[1].Value.Trim().TrimEnd(',', '.');
            string right = veMatch.Groups[2].Value.Trim().TrimStart(',', '.');

            if (HasVerbEnding(left) && HasVerbEnding(right))
            {
                var leftList = DecomposePlayerActions(left);
                result.AddRange(leftList.Count > 0 ? leftList : new List<string> { left });

                var rightList = DecomposePlayerActions(right);
                result.AddRange(rightList.Count > 0 ? rightList : new List<string> { right });

                return CapActions(result, 3);
            }
        }

        return new List<string> { trimmed };
    }

    private static string ConjugateZarfFiil(string phrase)
    {
        if (string.IsNullOrWhiteSpace(phrase))
            return phrase;

        string[] words = phrase.Trim().TrimEnd(',', '.', '!', '?').Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
            return phrase;

        words[^1] = ConvertLastWordZarf(words[^1]);
        string joined = string.Join(" ", words);

        var trCulture = new System.Globalization.CultureInfo("tr-TR");
        return char.ToUpper(joined[0], trCulture) + joined.Substring(1);
    }

    private static string ConvertLastWordZarf(string word)
    {
        if (string.IsNullOrWhiteSpace(word))
            return word;

        string w = word.Trim();
        string lower = w.ToLowerInvariant();

        if (lower == "deyip" || lower == "diyip") return "diyorum";
        if (lower == "gidip") return "gidiyorum";
        if (lower == "gelip") return "geliyorum";
        if (lower == "alıp") return "alıyorum";
        if (lower == "verip") return "veriyorum";
        if (lower == "bakıp") return "bakıyorum";
        if (lower == "açıp") return "açıyorum";
        if (lower == "çekip") return "çekiyorum";
        if (lower == "çıkıp") return "çıkıyorum";
        if (lower == "girip") return "giriyorum";
        if (lower == "sorup") return "soruyorum";
        if (lower == "oturup") return "oturuyorum";
        if (lower == "kalkıp") return "kalkıyorum";
        if (lower == "uzanıp") return "uzanıyorum";
        if (lower == "uzatıp") return "uzatıyorum";
        if (lower == "götürüp") return "götürüyorum";
        if (lower == "getirip") return "getiriyorum";
        if (lower == "gösterip") return "gösteriyorum";

        if (lower.EndsWith("layıp")) return w.Substring(0, w.Length - 5) + "lıyorum";
        if (lower.EndsWith("leyip")) return w.Substring(0, w.Length - 5) + "liyorum";

        if (lower.EndsWith("yıp")) return w.Substring(0, w.Length - 3) + "yorum";
        if (lower.EndsWith("yip")) return w.Substring(0, w.Length - 3) + "iyorum";
        if (lower.EndsWith("yup")) return w.Substring(0, w.Length - 3) + "yorum";
        if (lower.EndsWith("yüp")) return w.Substring(0, w.Length - 3) + "yorum";

        if (lower.EndsWith("ıp")) return w.Substring(0, w.Length - 2) + "ıyorum";
        if (lower.EndsWith("ip")) return w.Substring(0, w.Length - 2) + "iyorum";
        if (lower.EndsWith("up")) return w.Substring(0, w.Length - 2) + "uyorum";
        if (lower.EndsWith("üp")) return w.Substring(0, w.Length - 2) + "üyorum";

        return w;
    }

    private static bool HasVerbEnding(string phrase)
    {
        if (string.IsNullOrWhiteSpace(phrase)) return false;
        string[] words = phrase.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return false;
        string last = words[^1].ToLowerInvariant().TrimEnd('.', ',', '!', '?');
        string[] verbSuffixes =
        {
            "dim", "dım", "dum", "düm", "tim", "tım", "tum", "tüm",
            "yorum", "yorsun", "yoruz",
            "rim", "rım", "rum", "rüm", "erim", "arım", "urum", "ürüm",
            "yim", "yım", "eceğim", "acağım"
        };
        return verbSuffixes.Any(s => last.EndsWith(s) && last.Length >= s.Length + 2);
    }

    private static List<string> CapActions(List<string> actions, int max)
    {
        var cleaned = actions
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .Select(a => a.Trim())
            .Distinct()
            .ToList();

        if (cleaned.Count <= max)
            return cleaned;

        var result = cleaned.Take(max - 1).ToList();
        result.Add(string.Join(" ve ", cleaned.Skip(max - 1)));
        return result;
    }

    private static readonly string[] ImportantFactKeywords =
    {
        "harita", "gizli giriş", "yan dehliz", "tuzak", "eldrin", "kael", "varo", "elara", "sandık", "50 altın", "çökmüş havalandırma", "kızıl kuzgun"
    };

    /// <summary>
    /// Deterministically extracts facts, quests, and event log entries from the turn dialogue so 4B models never lose core memory.
    /// Returns true if an active quest was added.
    /// </summary>
    public static bool AutoExtractTurnMemory(string rawPlayerInput, string cleanAiNarrative, CampaignMemory memory)
    {
        if (memory == null)
            return false;

        bool questAdded = false;
        string aiLower = (cleanAiNarrative ?? "").ToLowerInvariant();

        // 1. Auto Quest Extraction for the Silver Mine contract
        // Tetikleme SADECE DM'in cevabı (cleanAiNarrative) içinde görev içeriği geçtiğinde yapılmalıdır:
        bool mentionsParchmentOrQuest = aiLower.Contains("gümüş maden") ||
                                       aiLower.Contains("50 altın") ||
                                       aiLower.Contains("tüccar sandığı") ||
                                       ((aiLower.Contains("parşömen") || aiLower.Contains("paşömen") || aiLower.Contains("ilan")) && aiLower.Contains("ödül"));

        if (mentionsParchmentOrQuest)
        {
            if (!memory.ActiveQuests.Any(q => q.Contains("Gümüş Maden", StringComparison.OrdinalIgnoreCase)))
            {
                memory.AddQuest(QuestSilverMine);
                questAdded = true;
            }
        }

        // 2. Auto Turn Event (compact snippet)
        if (!string.IsNullOrWhiteSpace(rawPlayerInput) || !string.IsNullOrWhiteSpace(cleanAiNarrative))
        {
            string shortPlayer = (rawPlayerInput ?? "").Trim();
            if (shortPlayer.Length > 50) shortPlayer = shortPlayer.Substring(0, 47) + "...";

            string shortAi = "";
            if (!string.IsNullOrWhiteSpace(cleanAiNarrative))
            {
                int dotIdx = cleanAiNarrative.IndexOfAny(new[] { '.', '!', '?' });
                shortAi = dotIdx > 0 ? cleanAiNarrative.Substring(0, dotIdx + 1).Trim() : cleanAiNarrative.Trim();
                if (shortAi.Length > 70) shortAi = shortAi.Substring(0, 67) + "...";
            }

            string eventLine = string.IsNullOrEmpty(shortPlayer)
                ? $"DM: {shortAi}"
                : (string.IsNullOrEmpty(shortAi) ? $"Oyuncu: {shortPlayer}" : $"Oyuncu: {shortPlayer} -> DM: {shortAi}");

            if (eventLine.Length > 140)
                eventLine = eventLine.Substring(0, 137) + "...";

            memory.AddEvent(eventLine);
        }

        // 3. Extract Important Lore / Clue Facts
        if (!string.IsNullOrWhiteSpace(cleanAiNarrative))
        {
            string[] sentences = Regex.Split(cleanAiNarrative, @"(?<=[.!?])\s+");
            foreach (string sentence in sentences)
            {
                string sLower = sentence.ToLowerInvariant();
                foreach (string kw in ImportantFactKeywords)
                {
                    if (sLower.Contains(kw))
                    {
                        string cleanSentence = sentence.Trim();
                        if (cleanSentence.Length > 12 && cleanSentence.Length < 180)
                        {
                            memory.AddFact(cleanSentence);
                            break;
                        }
                    }
                }
            }
        }

        return questAdded;
    }
}
