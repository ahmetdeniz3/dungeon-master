using Godot;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

public partial class Main : Control
{
    private const string OllamaUrl = "http://127.0.0.1:11434/api/chat";
    private const string OllamaModel = "gemma4:12b";
    private const string DefaultCharacterId = "test_character";
    private const int MaxHistoryMessages = 24;
    private const int ArchiveBatchSize = 4;

    private static readonly System.Net.Http.HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromMinutes(3)
    };

    private RichTextLabel _chat;
    private LineEdit _input;
    private Button _sendButton;

    private RichTextLabel _charSummary;
    private RichTextLabel _questView;
    private RichTextLabel _memoryView;
    private OptionButton _skillSelector;
    private Button _quickRollButton;
    private Button _testCombatButton;
    private Button _newCharacterButton;
    private Button _resetCampaignButton;
    private Button _shortRestButton;
    private Button _longRestButton;
    private Button _deathSaveButton;

    private ConfirmationDialog _charCreateDialog;
    private LineEdit _charNameInput;
    private OptionButton _charClassOption;

    private HBoxContainer _pendingRollBar;
    private Label _pendingRollLabel;
    private Button _pendingRollButton;

    private CombatUI _combatUI;

    private CharacterData _player;
    private PendingRollRequest _pendingRoll;
    private ActionAnalysis _lastPlayerAnalysis;
    private string _lastRawPlayerText = "";
    private readonly Queue<(string StepText, int StepIndex, int TotalSteps)> _pendingActionSteps = new();
    private string _currentStepText = "";
    private int _currentStepIndex = 1;
    private int _currentTotalSteps = 1;
    private bool _isWaitingForAi;
    private bool _isSummarizing;
    private CancellationTokenSource _requestCts;
    private readonly CancellationTokenSource _lifetimeCts = new();

    private readonly CampaignMemory _memory = new();
    private readonly StringBuilder _committedChat = new();
    private readonly List<Dictionary<string, string>> _conversationHistory = new();

    private readonly ConcurrentQueue<string> _incomingStreamChunks = new();
    private readonly StringBuilder _streamDisplayBuffer = new();
    private bool _streamStartedHeader;
    private bool _streamInsideBracket;
    private bool _streamNetworkFinished;
    private string _streamFinalRawAnswer;
    private string _streamPendingError;
    private double _streamCharCarry;

    public override void _Ready()
    {
        _chat = GetNode<RichTextLabel>("RootMargin/Layout/StoryPanel/StoryMargin/StoryVBox/Chat");
        _input = GetNode<LineEdit>("RootMargin/Layout/StoryPanel/StoryMargin/StoryVBox/InputRow/Input");
        _sendButton = GetNode<Button>("RootMargin/Layout/StoryPanel/StoryMargin/StoryVBox/InputRow/SendButton");

        _charSummary = GetNode<RichTextLabel>("RootMargin/Layout/CharacterPanel/CharMargin/CharVBox/CharSummary");
        _questView = GetNode<RichTextLabel>("RootMargin/Layout/CharacterPanel/CharMargin/CharVBox/QuestView");
        _memoryView = GetNode<RichTextLabel>("RootMargin/Layout/CharacterPanel/CharMargin/CharVBox/MemoryView");
        _skillSelector = GetNode<OptionButton>("RootMargin/Layout/CharacterPanel/CharMargin/CharVBox/SkillSelector");
        _quickRollButton = GetNode<Button>("RootMargin/Layout/CharacterPanel/CharMargin/CharVBox/QuickRollButton");
        _testCombatButton = GetNode<Button>("RootMargin/Layout/CharacterPanel/CharMargin/CharVBox/TestCombatButton");
        _newCharacterButton = GetNode<Button>("RootMargin/Layout/CharacterPanel/CharMargin/CharVBox/CampaignRow/NewCharacterButton");
        _resetCampaignButton = GetNode<Button>("RootMargin/Layout/CharacterPanel/CharMargin/CharVBox/CampaignRow/ResetCampaignButton");
        _shortRestButton = GetNode<Button>("RootMargin/Layout/CharacterPanel/CharMargin/CharVBox/RestRow/ShortRestButton");
        _longRestButton = GetNode<Button>("RootMargin/Layout/CharacterPanel/CharMargin/CharVBox/RestRow/LongRestButton");
        _deathSaveButton = GetNode<Button>("RootMargin/Layout/CharacterPanel/CharMargin/CharVBox/DeathSaveButton");

        _charCreateDialog = GetNode<ConfirmationDialog>("CharacterCreateDialog");
        _charNameInput = GetNode<LineEdit>("CharacterCreateDialog/VBox/CharNameInput");
        _charClassOption = GetNode<OptionButton>("CharacterCreateDialog/VBox/CharClassOption");
        SetupCharacterCreateDialog();

        _pendingRollBar = GetNode<HBoxContainer>("RootMargin/Layout/StoryPanel/StoryMargin/StoryVBox/PendingRollBar");
        _pendingRollLabel = GetNode<Label>("RootMargin/Layout/StoryPanel/StoryMargin/StoryVBox/PendingRollBar/PendingRollLabel");
        _pendingRollButton = GetNode<Button>("RootMargin/Layout/StoryPanel/StoryMargin/StoryVBox/PendingRollBar/PendingRollButton");

        _combatUI = GetNode<CombatUI>("RootMargin/Layout/CombatUI");

        _sendButton.Pressed += OnSendPressed;
        _input.TextSubmitted += _ => OnSendPressed();
        _quickRollButton.Pressed += OnQuickRollPressed;
        _pendingRollButton.Pressed += OnPendingRollPressed;
        _testCombatButton.Pressed += OnTestCombatPressed;
        _newCharacterButton.Pressed += OnNewCharacterPressed;
        _resetCampaignButton.Pressed += OnResetCampaignPressed;
        _charCreateDialog.Confirmed += OnCharacterCreateConfirmed;
        _shortRestButton.Pressed += () => ExecuteRest(isLongRest: false, notifyAi: true);
        _longRestButton.Pressed += () => ExecuteRest(isLongRest: true, notifyAi: true);
        _deathSaveButton.Pressed += OnDeathSavePressed;

        _combatUI.CombatEnded += OnCombatEnded;
        _combatUI.EquipmentChanged += OnEquipmentChanged;
        _combatUI.StoryNarrativeEvent += OnCombatStoryNarrative;

        _player = DataManager.Instance?.GetCharacterData(DefaultCharacterId);
        SetupSkillSelector();
        LoadOrInitializeCampaign();

        GD.Print("MAIN HAZIR");
    }

    public override void _ExitTree()
    {
        _requestCts?.Cancel();
        _requestCts?.Dispose();
        _lifetimeCts.Cancel();
        _lifetimeCts.Dispose();
    }

    public override void _Process(double delta)
    {
        while (_incomingStreamChunks.TryDequeue(out string chunk))
        {
            foreach (char ch in chunk)
            {
                if (ch == '[')
                {
                    _streamInsideBracket = true;
                    continue;
                }

                if (_streamInsideBracket)
                {
                    if (ch == ']')
                        _streamInsideBracket = false;
                    continue;
                }

                _streamDisplayBuffer.Append(ch);
            }
        }

        if (_streamDisplayBuffer.Length > 0)
        {
            if (!_streamStartedHeader)
            {
                _streamStartedHeader = true;
                _chat.Text = _committedChat.ToString();
                string headerTag = _currentTotalSteps > 1
                    ? $"[b]DM (Adım {_currentStepIndex}/{_currentTotalSteps}):[/b] "
                    : "[b]DM:[/b] ";
                _chat.AppendText(_committedChat.Length > 0 ? "\n" + headerTag : headerTag);
            }

            double charsPerSec = _streamNetworkFinished ? 180.0 : 85.0;
            _streamCharCarry += delta * charsPerSec;
            int emitCount = Math.Min(_streamDisplayBuffer.Length, (int)_streamCharCarry);

            if (emitCount > 0)
            {
                _streamCharCarry -= emitCount;
                string slice = _streamDisplayBuffer.ToString(0, emitCount);
                _streamDisplayBuffer.Remove(0, emitCount);
                _chat.AppendText(EscapeBbcode(slice));
            }
        }

        if (_streamNetworkFinished && _incomingStreamChunks.IsEmpty && _streamDisplayBuffer.Length == 0)
        {
            _streamNetworkFinished = false;
            string error = _streamPendingError;
            string finalAnswer = _streamFinalRawAnswer;
            _streamPendingError = null;
            _streamFinalRawAnswer = null;

            if (!string.IsNullOrEmpty(error))
                OnAiError(error);
            else
                OnAiCompleted(finalAnswer ?? "");
        }
    }

    // =========================================================
    // CAMPAIGN INIT / SAVE / RESET
    // =========================================================

    private void LoadOrInitializeCampaign()
    {
        _committedChat.Clear();
        _conversationHistory.Clear();
        _memory.Clear();
        ClearPendingRoll();
        _pendingActionSteps.Clear();
        _currentStepText = "";
        _currentStepIndex = 1;
        _currentTotalSteps = 1;

        while (_incomingStreamChunks.TryDequeue(out _)) { }
        _streamDisplayBuffer.Clear();
        _streamStartedHeader = false;
        _streamInsideBracket = false;
        _streamNetworkFinished = false;
        _streamFinalRawAnswer = null;
        _streamPendingError = null;

        CampaignSavePayload saved = _memory.LoadFromDisk();
        if (saved != null && saved.ConversationHistory != null && saved.ConversationHistory.Count > 0)
        {
            if (_player != null)
            {
                if (!string.IsNullOrWhiteSpace(saved.PlayerName))
                    _player.Name = saved.PlayerName;

                if (!string.IsNullOrWhiteSpace(saved.PlayerClassId))
                    _player.ClassId = saved.PlayerClassId;

                if (saved.PlayerAbilityScores != null)
                    _player.AbilityScores = saved.PlayerAbilityScores;

                if (saved.PlayerWeapons != null && saved.PlayerWeapons.Count > 0)
                    _player.WeaponInventory = new List<string>(saved.PlayerWeapons);

                if (saved.PlayerArmors != null)
                    _player.ArmorInventory = new List<string>(saved.PlayerArmors);

                if (saved.PlayerKnownSpells != null)
                    _player.KnownSpells = new List<string>(saved.PlayerKnownSpells);

                if (saved.PlayerSpellSlotsMax != null)
                    _player.SpellSlotsMax = new Dictionary<int, int>(saved.PlayerSpellSlotsMax);

                if (saved.PlayerSpellSlotsCurrent != null)
                    _player.SpellSlotsCurrent = new Dictionary<int, int>(saved.PlayerSpellSlotsCurrent);

                if (!string.IsNullOrWhiteSpace(saved.PlayerSpellcastingAbility))
                    _player.SpellcastingAbility = saved.PlayerSpellcastingAbility;

                if (saved.PlayerHitDieSides > 0)
                    _player.HitDieSides = saved.PlayerHitDieSides;

                if (saved.PlayerLevel > 0)
                    _player.Level = saved.PlayerLevel;

                if (saved.PlayerMaxHP > 0)
                    _player.MaximumHP = saved.PlayerMaxHP;

                _player.ExperiencePoints = Math.Max(0, saved.PlayerXP);

                if (saved.PlayerHitDiceRemaining >= 0)
                    _player.HitDiceRemaining = Math.Clamp(saved.PlayerHitDiceRemaining, 0, _player.Level);

                if (saved.PlayerCurrentHP >= 0)
                    _player.CurrentHP = Math.Clamp(saved.PlayerCurrentHP, 0, _player.MaximumHP);

                _player.ActiveConditions.Clear();
                if (saved.PlayerConditions != null)
                {
                    foreach (string cond in saved.PlayerConditions)
                    {
                        if (DataManager.Instance?.GetConditionData(cond) != null)
                            _player.ActiveConditions.Add(cond);
                    }
                }

                if (saved.PlayerGold >= 0)
                    _player.Gold = saved.PlayerGold;

                _player.HasShieldInInventory = saved.PlayerHasShield;
                _player.EquippedShieldId = saved.PlayerEquippedShieldId ?? "";

                if (saved.PlayerItems != null)
                    _player.Items = new Dictionary<string, int>(saved.PlayerItems);

                if (!string.IsNullOrEmpty(saved.PlayerEquippedWeaponId))
                    _player.EquippedWeaponId = saved.PlayerEquippedWeaponId;

                _player.EquippedArmorId = saved.PlayerEquippedArmorId ?? "";
                ArmorData armor = !string.IsNullOrEmpty(_player.EquippedArmorId)
                    ? DataManager.Instance?.GetArmorData(_player.EquippedArmorId)
                    : null;
                _player.ArmorClass = ArmorClassCalculator.Calculate(_player, armor);
                _player.InitiativeBonus = AbilityScoreCalculator.GetModifier(_player.AbilityScores.Dexterity);
            }

            foreach (var msg in saved.ConversationHistory)
            {
                _conversationHistory.Add(msg);
                string role = msg.TryGetValue("role", out string r) ? r : "";
                string content = msg.TryGetValue("content", out string c) ? c : "";

                if (role == "assistant")
                {
                    string clean = StructuredDmResponse.TryParse(content, out StructuredDmResponse dm)
                        ? dm.Anlatim
                        : AIDMDirectiveParser.Parse(content).CleanNarrative;
                    if (!string.IsNullOrEmpty(clean))
                        CommitChatLine($"[b]DM:[/b] {EscapeBbcode(clean)}");
                }
                else if (role == "user" && !content.StartsWith("[SİSTEM", StringComparison.Ordinal) && !content.StartsWith("[ZAR", StringComparison.Ordinal))
                {
                    string displayUser = ExtractRawUserDisplay(content);
                    CommitChatLine($"[b]Sen:[/b] {EscapeBbcode(displayUser)}");
                }
            }

            CommitChatLine("[color=gray]--- Kayıtlı macera yüklendi ---[/color]");

            string recentLog = AdventureLogger.ReadRecentLogForPrompt(5);
            if (string.IsNullOrWhiteSpace(recentLog))
            {
                AdventureLogger.ClearAndInit();
                foreach (var msg in saved.ConversationHistory)
                {
                    string role = msg.TryGetValue("role", out string r) ? r : "";
                    string content = msg.TryGetValue("content", out string c) ? c : "";
                    if (role == "assistant")
                    {
                        string clean = StructuredDmResponse.TryParse(content, out StructuredDmResponse dm)
                            ? dm.Anlatim
                            : AIDMDirectiveParser.Parse(content).CleanNarrative;
                        if (!string.IsNullOrWhiteSpace(clean))
                            AdventureLogger.AppendDm(clean);
                    }
                    else if (role == "user")
                    {
                        if (content.StartsWith("[SİSTEM", StringComparison.Ordinal) || content.StartsWith("[ZAR", StringComparison.Ordinal))
                            AdventureLogger.AppendSystem(content);
                        else
                            AdventureLogger.AppendPlayer(ExtractRawUserDisplay(content));
                    }
                }
            }
        }
        else
        {
            string intro = "Yağmurlu bir akşamda Kırık Kalkan Tavernası'nın ağır meşe kapısını itip içeri giriyorsun. Tezgâhta yaşlı hancı Bram bardak siliyor, kapı yanındaki masada deri zırhlı iki haydut sessizce zar atıyor; arka köşede ise kukuletalı bir yabancı yıpranmış bir haritaya eğilmiş. İlan panosunda da kırmızı mühürlü bir parşömen dikkatini çekiyor.";
            CommitChatLine($"[b]DM:[/b] {EscapeBbcode(intro)}");
            _memory.SceneEntities = "Hancı Bram (tezgâhta bardak siliyor), İki haydut (kapı yanındaki masada zar atıyor), Kukuletalı yabancı (arka köşede haritaya eğilmiş), Kırmızı mühürlü parşömen (ilan panosunda asılı)";
            _conversationHistory.Add(new Dictionary<string, string>
            {
                { "role", "assistant" },
                { "content", intro }
            });
            _memory.AddEvent("Karakter yağmurlu bir akşamda Kırık Kalkan Tavernası'na girdi.");
            AdventureLogger.ClearAndInit(intro);
        }

        RefreshCharacterSummary();
        RefreshMemoryView();
        _combatUI.SetStandbyState(_player);
    }

    private void OnResetCampaignPressed()
    {
        if (_isWaitingForAi)
            return;

        _requestCts?.Cancel();
        _memory.DeleteSaveFile();
        AdventureLogger.ClearAndInit();

        if (_player != null)
        {
            if (_player.Id == "custom_hero")
            {
                _player = CharacterPresets.CreateByPreset(_player.Name, _player.ClassId);
            }
            else
            {
                _player = DataManager.Instance?.GetCharacterData(DefaultCharacterId);
                if (_player != null)
                {
                    _player.Level = 1;
                    _player.ExperiencePoints = 0;
                    _player.MaximumHP = 12;
                    _player.CurrentHP = _player.MaximumHP;
                    _player.HitDiceRemaining = 1;
                    _player.DeathSaveSuccesses = 0;
                    _player.DeathSaveFailures = 0;
                    _player.ActiveConditions.Clear();
                    _player.Gold = 15;
                }
            }
        }

        LoadOrInitializeCampaign();
    }

    private void SetupCharacterCreateDialog()
    {
        if (_charClassOption == null)
            return;

        _charClassOption.Clear();
        _charClassOption.AddItem("Fighter (Savaşçı - d10 HP, Zırh & Silah Ustası)");
        _charClassOption.SetItemMetadata(0, "fighter");

        _charClassOption.AddItem("Rogue (Hırsız - d8 HP, Çevik & Gizlilik)");
        _charClassOption.SetItemMetadata(1, "rogue");

        _charClassOption.AddItem("Wizard (Büyücü - d6 HP, Büyü & Cantrip)");
        _charClassOption.SetItemMetadata(2, "wizard");
    }

    private void OnNewCharacterPressed()
    {
        if (_isWaitingForAi || _charCreateDialog == null)
            return;

        _charNameInput.Text = "";
        _charCreateDialog.PopupCentered();
    }

    private void OnCharacterCreateConfirmed()
    {
        string charName = _charNameInput.Text.Trim();
        if (string.IsNullOrEmpty(charName))
            charName = "Maceracı";

        string classId = _charClassOption.Selected >= 0
            ? _charClassOption.GetItemMetadata(_charClassOption.Selected).AsString()
            : "fighter";

        _requestCts?.Cancel();
        _memory.DeleteSaveFile();

        _player = CharacterPresets.CreateByPreset(charName, classId);
        LoadOrInitializeCampaign();
        SaveCampaignState();
    }

    private void OnEquipmentChanged()
    {
        RefreshCharacterSummary();
        SaveCampaignState();
    }

    private void SaveCampaignState()
    {
        _memory.SaveToDisk(_player, _conversationHistory);
        RefreshMemoryView();
    }

    // =========================================================
    // REST & DEATH SAVES
    // =========================================================

    private void ExecuteRest(bool isLongRest, bool notifyAi)
    {
        if (_player == null || (_combatUI != null && _combatUI.IsCombatActive))
        {
            CommitChatLine("[color=orange]Savaş sırasında dinlenemezsin![/color]");
            return;
        }

        RestResult rest = isLongRest
            ? RestAndProgressionSystem.PerformLongRest(_player)
            : RestAndProgressionSystem.PerformShortRest(_player);

        if (rest == null)
            return;

        CommitChatLine($"[color=green]{EscapeBbcode(rest.Summary)}[/color]");
        _memory.AddEvent(rest.Summary);
        RefreshCharacterSummary();
        SaveCampaignState();

        if (notifyAi && !_isWaitingForAi)
        {
            string whoIsHere = string.IsNullOrWhiteSpace(_memory.SceneEntities)
                ? "Şu an yanında bilinen kimse yok"
                : $"Sahnede sadece şunlar var: {_memory.SceneEntities}";
            AddUserMessageAndSend(
                $"[SİSTEM DİNLENME] {rest.Summary}. Mevcut konum: '{_memory.CurrentLocation}'. ({whoIsHere}). Dinlenme sonrası SADECE şu anki konumdaki ({_memory.CurrentLocation}) ortamı kısaca anlat; eğer önceki mekandan (ör. tavernadan) ayrıldıysan oradaki haydutları, hancıyı veya kukuletalı yabancıyı ASLA buraya getirme.",
                echoToChat: false
            );
        }
    }

    private void OnDeathSavePressed()
    {
        if (_player == null || _player.CurrentHP > 0 || _isWaitingForAi)
            return;

        DeathSaveResult res = RestAndProgressionSystem.RollDeathSave(_player);
        if (res == null)
            return;

        CommitChatLine($"[color=orange]{EscapeBbcode(res.Summary)}[/color]");
        _memory.AddEvent(res.Summary);
        RefreshCharacterSummary();
        SaveCampaignState();

        AddUserMessageAndSend($"[SİSTEM ÖLÜM KURTARMA ZARI] {res.Summary}. Duruma göre sahneyi devam ettir.", echoToChat: false);
    }

    // =========================================================
    // CHAT DISPLAY HELPERS
    // =========================================================

    private void CommitChatLine(string bbcodeLine)
    {
        if (_committedChat.Length > 0)
            _committedChat.Append('\n');
        _committedChat.AppendLine(bbcodeLine);
        _chat.Text = _committedChat.ToString();
    }

    private static string EscapeBbcode(string text)
    {
        return string.IsNullOrEmpty(text) ? "" : text.Replace("[", "[lb]");
    }

    // =========================================================
    // CHARACTER PANEL, MEMORY VIEW & QUICK ROLLS
    // =========================================================

    private void SetupSkillSelector()
    {
        _skillSelector.Clear();

        foreach (string skill in SkillCalculator.AllSkills.OrderBy(s => s))
        {
            _skillSelector.AddItem($"Beceri: {FormatLabel(skill)}");
            _skillSelector.SetItemMetadata(_skillSelector.ItemCount - 1, $"skill:{skill}");
        }

        string[] abilities = { "strength", "dexterity", "constitution", "intelligence", "wisdom", "charisma" };
        foreach (string ability in abilities)
        {
            _skillSelector.AddItem($"Kurtarma: {FormatLabel(ability)}");
            _skillSelector.SetItemMetadata(_skillSelector.ItemCount - 1, $"save:{ability}");
        }
    }

    private void RefreshCharacterSummary()
    {
        if (_charSummary == null)
            return;

        if (_player == null)
        {
            _charSummary.Text = "[color=red]Karakter yüklenemedi.[/color]";
            return;
        }

        string weaponName = DataManager.Instance?.GetWeaponData(_player.EquippedWeaponId)?.Name ?? "-";
        string armorName = DataManager.Instance?.GetArmorData(_player.EquippedArmorId)?.Name ?? "Zırhsız";

        string FormatMod(int score)
        {
            int mod = AbilityScoreCalculator.GetModifier(score);
            return mod >= 0 ? $"+{mod}" : mod.ToString();
        }

        var proficientSkills = _player.Skills
            .Where(kv => kv.Value.Proficient)
            .Select(kv => kv.Value.Expertise ? $"{FormatLabel(kv.Key)} (x2)" : FormatLabel(kv.Key))
            .ToList();

        int nextXp = RestAndProgressionSystem.GetNextLevelXP(_player.Level);

        var sb = new StringBuilder();
        sb.AppendLine($"[b]{EscapeBbcode(_player.Name)}[/b] ({FormatLabel(_player.ClassId)} Sv. {_player.Level} | XP: {_player.ExperiencePoints}/{nextXp})");
        sb.AppendLine($"HP: [b]{_player.CurrentHP}/{_player.MaximumHP}[/b] | AC: [b]{_player.ArmorClass}[/b] | HD: {_player.HitDiceRemaining}/{_player.Level}");
        if (_player.SpellSlotsMax.TryGetValue(1, out int maxSlot1) && maxSlot1 > 0)
        {
            int curSlot1 = _player.SpellSlotsCurrent.TryGetValue(1, out int cs) ? cs : 0;
            sb.AppendLine($"Büyü Slotları (Sv.1): {curSlot1}/{maxSlot1}");
        }
        sb.AppendLine($"Hız: {_player.WalkingSpeed} ft | İnisiyatif: +{_player.InitiativeBonus} | Altın: [b]{_player.Gold} GP[/b]");
        string shieldTag = string.IsNullOrEmpty(_player.EquippedShieldId) ? "" : " (+Kalkan)";
        sb.AppendLine($"Silah: {EscapeBbcode(weaponName)} | Zırh: {EscapeBbcode(armorName)}{shieldTag}");
        if (_player.Items.Count > 0)
        {
            var itemNames = _player.Items.Select(kv =>
            {
                string nm = DataManager.Instance?.GetItemData(kv.Key)?.Name ?? FormatLabel(kv.Key);
                return $"{nm} x{kv.Value}";
            });
            sb.AppendLine($"Çanta: {string.Join(", ", itemNames)}");
        }
        sb.AppendLine($"STR {_player.AbilityScores.Strength} ({FormatMod(_player.AbilityScores.Strength)}) | DEX {_player.AbilityScores.Dexterity} ({FormatMod(_player.AbilityScores.Dexterity)}) | CON {_player.AbilityScores.Constitution} ({FormatMod(_player.AbilityScores.Constitution)})");
        sb.AppendLine($"INT {_player.AbilityScores.Intelligence} ({FormatMod(_player.AbilityScores.Intelligence)}) | WIS {_player.AbilityScores.Wisdom} ({FormatMod(_player.AbilityScores.Wisdom)}) | CHA {_player.AbilityScores.Charisma} ({FormatMod(_player.AbilityScores.Charisma)})");

        if (proficientSkills.Count > 0)
            sb.AppendLine($"[b]Yetkinlikler:[/b] {string.Join(", ", proficientSkills)}");

        if (_player.ActiveConditions.Count > 0)
            sb.AppendLine($"[color=orange][b]Durumlar:[/b] {string.Join(", ", _player.ActiveConditions.Select(FormatLabel))}[/color]");

        _charSummary.Text = sb.ToString().TrimEnd();

        if (_deathSaveButton != null)
            _deathSaveButton.Visible = _player.CurrentHP <= 0;
    }

    private void RefreshMemoryView()
    {
        if (_questView != null)
            _questView.Text = _memory.FormatQuestsForUI();

        if (_memoryView != null)
            _memoryView.Text = _memory.FormatForUI();
    }

    private void OnQuickRollPressed()
    {
        if (_player == null || _skillSelector.Selected < 0 || _isWaitingForAi)
            return;

        string meta = _skillSelector.GetItemMetadata(_skillSelector.Selected).AsString();
        string[] parts = meta.Split(':');
        if (parts.Length != 2)
            return;

        bool isSave = parts[0] == "save";
        string targetName = parts[1];
        ExecuteCheckAndSendToAi(isSave, targetName, dc: null);
    }

    private void OnPendingRollPressed()
    {
        if (_player == null || _pendingRoll == null || _isWaitingForAi)
            return;

        PendingRollRequest request = _pendingRoll;
        ClearPendingRoll();
        ExecuteCheckAndSendToAi(request.IsSavingThrow, request.TargetName, request.DifficultyClass);
    }

    private void ExecuteCheckAndSendToAi(bool isSavingThrow, string targetName, int? dc)
    {
        RollAdvantage advantage = (!isSavingThrow && CombatSystem.HasAttackDisadvantage(_player.ActiveConditions))
            ? RollAdvantage.Disadvantage
            : RollAdvantage.Normal;

        CheckResult result;
        if (isSavingThrow)
        {
            int score = SkillCalculator.GetAbilityScoreByName(_player.AbilityScores, targetName);
            result = SavingThrowCalculator.RollSavingThrow(_player, targetName, score, advantage);
        }
        else
        {
            string ability = SkillCalculator.GetAbilityForSkill(targetName);
            int score = SkillCalculator.GetAbilityScoreByName(_player.AbilityScores, ability);
            result = SkillCheckCalculator.RollSkillCheck(_player, targetName, score, advantage);
        }

        if (result == null)
            return;

        string label = FormatLabel(targetName);
        string kind = isSavingThrow ? "Kurtarma Zarı" : "Beceri Kontrolü";
        string modStr = result.Modifier >= 0 ? $"+{result.Modifier}" : result.Modifier.ToString();
        string dcText = dc.HasValue
            ? $" (DC {dc.Value} -> {(result.Total >= dc.Value ? "BAŞARILI" : "BAŞARISIZ")})"
            : "";

        string critText = result.IsCriticalSuccess ? " [lb]NAT 20![rb]" : (result.IsCriticalFailure ? " [lb]NAT 1![rb]" : "");
        string logMsg = $"🎲 {kind} ({label}): d20({result.DiceRoll}) {modStr} = {result.Total}{dcText}{critText}";

        CommitChatLine($"[color=yellow]{logMsg}[/color]");

        string plainRoll = logMsg.Replace("[lb]", "[").Replace("[rb]", "]");
        _memory.AddEvent(plainRoll);
        AdventureLogger.AppendSystem(plainRoll);

        string outcomeGuidance = dc.HasValue
            ? (result.Total >= dc.Value
                ? $"[ZAR SONUCU: BAŞARILI ({label} {result.Total} >= DC {dc.Value}). Oyuncu bu denemede başarılı oldu. Başarının somut sonucunu anlat, yeni zar isteme; beceri zarı tek başına düşman öldürmez.]"
                : $"[ZAR SONUCU: BAŞARISIZ ({label} {result.Total} < DC {dc.Value}). Oyuncu bu denemede başarısız oldu. Ortaya çıkan engeli veya tepkiyi anlat, yeni zar isteme.]")
            : $"[ZAR SONUCU: {plainRoll}. Bu sonucu (yüksekse başarı/avantaj, düşükse zorluk olarak) sahneye uygula. NOT: Beceri zarı tek başına düşmanı öldürmez; saldırı/çatışma varsa [COMBAT: enemies=...] ekle, aktif savaş varsa düşman ölümünü yazma.]";

        AddUserMessageAndSend(outcomeGuidance, echoToChat: false);
    }

    // =========================================================
    // COMBAT TRIGGERS & CALLBACKS
    // =========================================================

    private void OnTestCombatPressed()
    {
        StartCombatFromIds(new List<string> { "goblin" });
    }

    private void StartCombatFromIds(List<string> enemyIds)
    {
        if (_player == null || DataManager.Instance == null)
            return;

        if (_player.CurrentHP <= 0)
        {
            CommitChatLine("[color=red]0 HP iken yeni savaşa giremezsin! Önce Ölüm Kurtarma Zarı at veya dinlen.[/color]");
            return;
        }

        if (enemyIds == null || enemyIds.Count == 0)
            return;

        string rawLower = (_lastRawPlayerText ?? "").ToLowerInvariant();
        bool isBramTarget = rawLower.Contains("bram") || rawLower.Contains("hancı") || rawLower.Contains("hanci") || rawLower.Contains("barmen");
        bool isHaydutMentioned = rawLower.Contains("haydut") || rawLower.Contains("kael") || rawLower.Contains("varo");

        // Eğer oyuncu Bram'ı hedef aldıysa ve haydutlardan bahsetmediyse savaşa kesinlikle Bram girsin
        if (isBramTarget && !isHaydutMentioned)
        {
            enemyIds = new List<string> { "npc_bram" };
        }

        var monsters = new List<CombatMonster>();
        var counts = new Dictionary<string, int>();

        foreach (string rawId in enemyIds)
        {
            string id = rawId.Trim().ToLowerInvariant();
            string baseId = id switch
            {
                "npc_bram" => "bandit",
                "npc_eldrin" => "bandit",
                "npc_elara" => "bandit",
                _ => id
            };

            if (DataManager.Instance.GetMonsterData(baseId) == null)
                continue;

            counts[id] = counts.TryGetValue(id, out int c) ? c + 1 : 1;
        }

        var indexTracker = new Dictionary<string, int>();
        foreach (string rawId in enemyIds)
        {
            string id = rawId.Trim().ToLowerInvariant();
            string baseId = id;
            string customDisplayName = null;

            if (id == "npc_bram")
            {
                baseId = "bandit";
                customDisplayName = "Hancı Bram";
            }
            else if (id == "npc_eldrin")
            {
                baseId = "bandit";
                customDisplayName = "Korucu Eldrin";
            }
            else if (id == "npc_elara")
            {
                baseId = "bandit";
                customDisplayName = "Bayan Elara";
            }

            MonsterData data = DataManager.Instance.GetMonsterData(baseId);
            if (data == null)
                continue;

            int idx = indexTracker.TryGetValue(id, out int prev) ? prev + 1 : 1;
            indexTracker[id] = idx;

            string displayName;
            if (!string.IsNullOrEmpty(customDisplayName))
            {
                displayName = customDisplayName;
            }
            else if (id == "bandit")
            {
                bool isTavern = (_memory?.CurrentLocation ?? "").Contains("Taverna", StringComparison.OrdinalIgnoreCase);
                if (isTavern)
                {
                    displayName = idx == 1 ? "Haydut Kael" : (idx == 2 ? "Haydut Varo" : $"Haydut {idx}");
                }
                else
                {
                    displayName = counts[id] > 1 ? $"Haydut {idx}" : "Haydut";
                }
            }
            else
            {
                string baseName = string.Equals(data.Name, "Bandit", StringComparison.OrdinalIgnoreCase) ? "Haydut" : data.Name;
                displayName = counts[id] > 1 ? $"{baseName} {idx}" : baseName;
            }

            string instanceId = $"{id}_{idx}_{Guid.NewGuid().ToString("N").Substring(0, 4)}";
            monsters.Add(new CombatMonster(data, instanceId, displayName));
        }

        if (monsters.Count == 0)
        {
            CommitChatLine("[color=orange]Sistem: Savaş başlatılmak istendi ancak geçerli canavar verisi bulunamadı.[/color]");
            return;
        }

        string enemyNames = string.Join(", ", monsters.Select(m => m.DisplayName));
        if (_combatUI != null && _combatUI.IsCombatActive)
        {
            CommitChatLine($"[color=red]⚔ Takviye düşmanlar savaşa katıldı: {EscapeBbcode(enemyNames)}![/color]");
            _memory.AddEvent($"Savaşa katıldı: {enemyNames}");
            AdventureLogger.AppendSystem($"Takviye düşmanlar savaşa katıldı: {enemyNames}");
            SaveCampaignState();
            _combatUI.AddEnemies(monsters);
        }
        else
        {
            CommitChatLine($"[color=red]⚔ Savaş başladı: {EscapeBbcode(enemyNames)}![/color]");
            _memory.AddEvent($"Savaş başladı: {enemyNames}");
            AdventureLogger.AppendSystem($"Savaş başladı: {enemyNames}");
            SaveCampaignState();
            _combatUI?.StartEncounter(_player, monsters);
        }

        RefreshCharacterSummary();
    }

    private void OnCombatEnded(bool playerWon, CombatEncounter encounter)
    {
        string enemies = string.Join(", ", encounter.Monsters.Select(m => m.DisplayName));
        string outcome;

        if (encounter.PlayerFled)
        {
            outcome = "Oyuncu savaştan kaçtı";
            CommitChatLine($"[color=yellow]🏃 Savaş sona erdi: {outcome}.[/color]");
        }
        else if (playerWon)
        {
            outcome = "Oyuncu savaşı kazandı";
            int gainedXp = encounter.Monsters.Sum(m => m.Data.ChallengeXP);
            int lootGold = encounter.Monsters.Sum(m => Math.Max(2, m.Data.ChallengeXP / 10));
            _player.Gold += lootGold;
            int levelsGained = RestAndProgressionSystem.GrantExperience(_player, gainedXp);
            CommitChatLine($"[color=cyan]⚔ Savaş sona erdi: {outcome}. (+{gainedXp} XP, +{lootGold} GP)[/color]");

            if (levelsGained > 0)
            {
                string lvlMsg = $"🎉 SEVİYE ATLADIN! {_player.Name} artık Seviye {_player.Level} (Max HP: {_player.MaximumHP})!";
                CommitChatLine($"[color=gold]{lvlMsg}[/color]");
                _memory.AddFact(lvlMsg);
            }

            if (encounter.Monsters.Any(m => m.DisplayName.Contains("Kael", StringComparison.OrdinalIgnoreCase) ||
                                            m.DisplayName.Contains("Varo", StringComparison.OrdinalIgnoreCase) ||
                                            m.Data.Id.Equals("bandit", StringComparison.OrdinalIgnoreCase)))
            {
                if (!string.IsNullOrWhiteSpace(_memory.SceneEntities))
                {
                    _memory.SceneEntities = Regex.Replace(
                        _memory.SceneEntities,
                        @"[İi]ki haydut\s*\([^)]*\)",
                        "Haydut Kael ve Varo (yerde etkisiz halde yatıyor)",
                        RegexOptions.IgnoreCase
                    );
                    if (!_memory.SceneEntities.Contains("Kael ve Varo"))
                    {
                        _memory.SceneEntities += ", Haydut Kael ve Varo (yerde etkisiz halde yatıyor)";
                    }
                }
                else
                {
                    _memory.SceneEntities = "Haydut Kael ve Varo (yerde etkisiz halde yatıyor)";
                }
                _memory.FocusedEntity = "";
            }
        }
        else
        {
            outcome = "Oyuncu savaşta 0 HP'ye düştü";
            CommitChatLine($"[color=red]💀 {EscapeBbcode(_player.Name)} 0 HP'ye düştü! Ölüm Kurtarma Zarı atmalısın.[/color]");
        }

        RefreshCharacterSummary();
        _memory.AddFact($"{enemies} ile savaş: {outcome} (Kalan HP: {_player.CurrentHP}/{_player.MaximumHP})");
        _memory.AddEvent($"Savaş bitti: {outcome}");
        AdventureLogger.AppendSystem($"Savaş sona erdi: {outcome} ({enemies}). Oyuncu HP: {_player.CurrentHP}/{_player.MaximumHP}");

        string summary = $"[SİSTEM SAVAŞ SONUCU] {outcome}. Etkisiz hale getirilen düşmanlar: {enemies}. {_player.Name} kalan HP: {_player.CurrentHP}/{_player.MaximumHP}. Savaş sonrası sahneyi 2-3 cümleyle anlat (yenilen düşmanları konuşturma).";
        AddUserMessageAndSend(summary, echoToChat: false);
    }

    private void OnCombatStoryNarrative(string text)
    {
        if (!IsInstanceValid(this) || string.IsNullOrWhiteSpace(text))
            return;

        _memory.AddEvent($"Savaş Turu: {text.Trim()}");
        RefreshCharacterSummary();
        if (!_isWaitingForAi)
            CommitChatLine($"[color=#c8dcff][b]DM (Tur):[/b] {EscapeBbcode(text)}[/color]");
    }

    // =========================================================
    // CHAT & OLLAMA STREAMING INTEGRATION
    // =========================================================

    private void OnSendPressed()
    {
        if (_isWaitingForAi)
            return;

        string text = _input.Text.Trim();
        if (string.IsNullOrEmpty(text))
            return;

        _input.Clear();
        ClearPendingRoll();
        _pendingActionSteps.Clear();

        CommitChatLine($"[b]Sen:[/b] {EscapeBbcode(text)}");
        AdventureLogger.AppendPlayer(text);

        List<string> steps = CampaignCanon.DecomposePlayerActions(text);
        if (steps == null || steps.Count == 0)
            steps = new List<string> { text };

        int total = steps.Count;
        for (int i = 0; i < total; i++)
        {
            _pendingActionSteps.Enqueue((steps[i], i + 1, total));
        }

        StartNextSequentialStep();
    }

    private void StartNextSequentialStep()
    {
        if (_pendingActionSteps.Count == 0)
            return;

        var (stepText, stepIndex, totalSteps) = _pendingActionSteps.Dequeue();
        _currentStepText = stepText;
        _currentStepIndex = stepIndex;
        _currentTotalSteps = totalSteps;

        _lastRawPlayerText = stepText;
        _memory.FocusedEntity = DmOrchestrator.ResolveFocusedEntity(stepText, _memory.SceneEntities, _memory.FocusedEntity);
        _lastPlayerAnalysis = ActionGatekeeper.Analyze(stepText, _player?.CurrentHP ?? 1);

        _conversationHistory.Add(new Dictionary<string, string>
        {
            { "role", "user" },
            { "content", stepText }
        });

        SaveCampaignState();
        _ = SendToAiAsync();
    }

    private static string ExtractRawUserDisplay(string content)
    {
        foreach (string prefix in new[] { "Oyuncu niyeti: \"", "Oyuncu mesajı: \"" })
        {
            int marker = content.IndexOf(prefix, StringComparison.Ordinal);
            if (marker >= 0)
            {
                string rest = content.Substring(marker + prefix.Length);
                return rest.EndsWith("\"") ? rest.Substring(0, rest.Length - 1) : rest;
            }
        }
        return content;
    }

    private void AddUserMessageAndSend(string text, bool echoToChat)
    {
        if (echoToChat)
        {
            CommitChatLine($"[b]Sen:[/b] {EscapeBbcode(text)}");
            AdventureLogger.AppendPlayer(text);
        }
        else if (text.StartsWith("[SİSTEM", StringComparison.Ordinal) || text.StartsWith("[ZAR", StringComparison.Ordinal))
        {
            AdventureLogger.AppendSystem(text);
        }

        _currentStepText = text;
        _currentStepIndex = 1;
        _currentTotalSteps = 1;

        _conversationHistory.Add(new Dictionary<string, string>
        {
            { "role", "user" },
            { "content", text }
        });

        SaveCampaignState();
        _ = SendToAiAsync();
    }

    private void ArchiveOverflowHistoryIfNeeded()
    {
        if (_conversationHistory.Count <= MaxHistoryMessages)
            return;

        int removeCount = Math.Min(ArchiveBatchSize, _conversationHistory.Count - (MaxHistoryMessages - ArchiveBatchSize));
        if (removeCount <= 0)
            return;

        List<Dictionary<string, string>> archived = _conversationHistory.GetRange(0, removeCount);
        _conversationHistory.RemoveRange(0, removeCount);

        var fallbackParts = new List<string>();
        foreach (var msg in archived)
        {
            string role = msg.TryGetValue("role", out string r) ? r : "";
            string content = msg.TryGetValue("content", out string c) ? c : "";
            if (role == "user" && !content.StartsWith("[SİSTEM", StringComparison.Ordinal))
            {
                string rawDisplay = ExtractRawUserDisplay(content);
                string shortText = rawDisplay.Length > 90 ? rawDisplay.Substring(0, 90) + "..." : rawDisplay;
                fallbackParts.Add(shortText);
            }
        }

        if (!_isSummarizing)
            _ = SummarizeArchivedTurnsAsync(archived, fallbackParts);
        else if (fallbackParts.Count > 0)
        {
            foreach (string part in fallbackParts)
                _memory.AddFact($"Oyuncu hareketi: {part}");
            SaveCampaignState();
        }
    }

    private async Task SummarizeArchivedTurnsAsync(
        List<Dictionary<string, string>> archivedMessages,
        List<string> fallbackParts)
    {
        _isSummarizing = true;
        try
        {
            var transcript = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(_memory.LongTermSummary))
                transcript.AppendLine($"MEVCUT ÖZET: {_memory.LongTermSummary}");

            transcript.AppendLine("YENİ ARŞİVLENECEK DİYALOG:");
            foreach (var msg in archivedMessages)
            {
                string role = msg.TryGetValue("role", out string r) && r == "user" ? "Oyuncu" : "DM";
                string raw = msg.TryGetValue("content", out string c) ? c : "";
                string content = role == "DM"
                    ? (StructuredDmResponse.TryParse(raw, out StructuredDmResponse dm) ? dm.Anlatim : AIDMDirectiveParser.Parse(raw).CleanNarrative)
                    : ExtractRawUserDisplay(raw);
                if (!string.IsNullOrWhiteSpace(content))
                    transcript.AppendLine($"{role}: {content}");
            }

            string modelToUse = await AgentLlmClient.GetPreferredModelAsync(_httpClient);
            var payload = new
            {
                model = modelToUse,
                think = false,
                messages = new[]
                {
                    new
                    {
                        role = "system",
                        content = "Sen bir D&D kampanya hafıza özetleyicisisin. Verilen mevcut özet ve yeni diyaloğu tek bir kısa Türkçe paragrafta (en fazla 3-4 cümle) birleştir. Oyuncunun aldığı kararları, NPC isimlerini, verilen sözleri, bulunan eşyaları/ipuçlarını ve yaptığı önemli hareketleri mutlaka koru. Sadece özeti yaz."
                    },
                    new
                    {
                        role = "user",
                        content = transcript.ToString()
                    }
                },
                stream = false,
                keep_alive = "10m",
                options = new { temperature = 0.4, num_ctx = 2048, num_predict = 150 }
            };

            using var req = new HttpRequestMessage(HttpMethod.Post, OllamaUrl)
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
            };

            using HttpResponseMessage resp = await _httpClient.SendAsync(req, _lifetimeCts.Token);
            if (!resp.IsSuccessStatusCode)
                throw new Exception($"HTTP {(int)resp.StatusCode}");

            string body = await resp.Content.ReadAsStringAsync(_lifetimeCts.Token);
            using JsonDocument doc = JsonDocument.Parse(body);
            string summary = doc.RootElement.GetProperty("message").GetProperty("content").GetString()?.Trim() ?? "";

            if (!string.IsNullOrWhiteSpace(summary))
            {
                Callable.From(() =>
                {
                    if (!IsInstanceValid(this))
                        return;

                    if (!string.IsNullOrWhiteSpace(_memory.LongTermSummary))
                    {
                        string combined = $"{_memory.LongTermSummary.Trim()} | {summary.Trim()}";
                        if (combined.Length > 600)
                            combined = combined.Substring(combined.Length - 600);
                        _memory.LongTermSummary = combined;
                    }
                    else
                    {
                        _memory.LongTermSummary = summary.Trim();
                    }

                    SaveCampaignState();
                    GD.Print("HAFIZA ÖZETİ GÜNCELLENDİ.");
                }).CallDeferred();
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"Arka plan özetleme atlandı ({ex.Message}), fallback uygulanıyor.");
            if (fallbackParts.Count > 0)
            {
                Callable.From(() =>
                {
                    if (!IsInstanceValid(this))
                        return;

                    foreach (string part in fallbackParts)
                        _memory.AddFact($"Oyuncu hareketi: {part}");
                    SaveCampaignState();
                }).CallDeferred();
            }
        }
        finally
        {
            _isSummarizing = false;
        }
    }

    private string BuildSystemPrompt()
    {
        bool isCombat = _combatUI != null && _combatUI.IsCombatActive;
        return CampaignCanon.BuildMicroStepSystemPrompt(
            _currentStepText,
            _lastPlayerAnalysis,
            _memory,
            _player,
            isCombat
        );
    }

    private async Task SendToAiAsync()
    {
        _requestCts?.Cancel();
        _requestCts?.Dispose();
        _requestCts = new CancellationTokenSource();
        CancellationToken ct = _requestCts.Token;

        while (_incomingStreamChunks.TryDequeue(out _)) { }
        _streamDisplayBuffer.Clear();
        _streamStartedHeader = false;
        _streamInsideBracket = false;
        _streamNetworkFinished = false;
        _streamFinalRawAnswer = null;
        _streamPendingError = null;
        _streamCharCarry = 0;

        SetAiBusy(true);
        string waitingText = _currentTotalSteps > 1
            ? $"[color=gray][DM Adım {_currentStepIndex}/{_currentTotalSteps} işleniyor: {EscapeBbcode(_currentStepText)}...][/color]"
            : "[color=gray][DM düşünüyor...][/color]";
        _chat.Text = _committedChat + "\n" + waitingText + "\n";
        string activeModel = await AgentLlmClient.GetPreferredModelAsync(_httpClient);
        GD.Print($"OLLAMA'YA İSTEK GÖNDERİLİYOR ({activeModel})");

        var messagesPayload = new List<Dictionary<string, string>>
        {
            new() { { "role", "system" }, { "content", BuildSystemPrompt() } }
        };

        bool isCombat = _combatUI != null && _combatUI.IsCombatActive;
        int curHp = _player?.CurrentHP ?? 1;
        int maxHp = _player?.MaximumHP ?? 1;

        int lastUserIdx = -1;
        for (int i = _conversationHistory.Count - 1; i >= 0; i--)
        {
            if (_conversationHistory[i].TryGetValue("role", out string r) && r == "user")
            {
                lastUserIdx = i;
                break;
            }
        }

        for (int i = 0; i < _conversationHistory.Count; i++)
        {
            var msg = _conversationHistory[i];
            string role = msg.TryGetValue("role", out string r) ? r : "user";
            string content = msg.TryGetValue("content", out string c) ? c : "";

            if (i == lastUserIdx && role == "user" &&
                !content.StartsWith("[SİSTEM", StringComparison.Ordinal) &&
                !content.StartsWith("[ZAR", StringComparison.Ordinal))
            {
                string framed = ActionGatekeeper.FramePromptForAi(
                    content,
                    _lastPlayerAnalysis,
                    isCombat,
                    curHp,
                    maxHp,
                    _memory.CurrentLocation,
                    _memory.SceneEntities,
                    _memory.FocusedEntity
                );
                messagesPayload.Add(new Dictionary<string, string>
                {
                    { "role", role },
                    { "content", framed }
                });
            }
            else
            {
                messagesPayload.Add(new Dictionary<string, string>
                {
                    { "role", role },
                    { "content", content }
                });
            }
        }

        var payloadObj = new
        {
            model = activeModel,
            think = false,
            messages = messagesPayload,
            stream = true,
            keep_alive = "10m",
            options = new
            {
                temperature = 0.45,
                top_p = 0.92,
                top_k = 50,
                min_p = 0.05,
                repeat_penalty = 1.18,
                num_ctx = 2048,
                num_predict = 220
            }
        };

        string jsonBody = JsonSerializer.Serialize(payloadObj);

        await Task.Run(async () =>
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, OllamaUrl)
                {
                    Content = new StringContent(jsonBody, Encoding.UTF8, "application/json")
                };

                using HttpResponseMessage response = await _httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    ct
                ).ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    string errorBody = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    GD.PrintErr($"Ollama HTTP {(int)response.StatusCode}: {errorBody}");
                    _streamPendingError = $"Ollama hata kodu {(int)response.StatusCode}: {errorBody}";
                    _streamNetworkFinished = true;
                    return;
                }

                using Stream stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 64);

                var fullContent = new StringBuilder();
                while (!ct.IsCancellationRequested)
                {
                    string line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
                    if (line == null)
                        break;

                    if (string.IsNullOrWhiteSpace(line))
                        continue;

                    using JsonDocument doc = JsonDocument.Parse(line);
                    JsonElement root = doc.RootElement;

                    if (root.TryGetProperty("message", out JsonElement msgEl) &&
                        msgEl.TryGetProperty("content", out JsonElement contentEl))
                    {
                        string chunk = contentEl.GetString() ?? "";
                        if (chunk.Length > 0)
                        {
                            fullContent.Append(chunk);
                            _incomingStreamChunks.Enqueue(chunk);
                        }
                    }

                    if (root.TryGetProperty("done", out JsonElement doneEl) && doneEl.GetBoolean())
                        break;
                }

                string finalAnswer = fullContent.ToString();
                GD.Print($"OLLAMA CEVABI TAMAMLANDI ({finalAnswer.Length} karakter)");
                _streamFinalRawAnswer = finalAnswer;
                _streamNetworkFinished = true;
            }
            catch (OperationCanceledException)
            {
                GD.Print("Ollama isteği iptal edildi.");
            }
            catch (Exception ex)
            {
                GD.PrintErr($"Ollama bağlantı hatası: {ex}");
                _streamPendingError = $"AI bağlantı hatası: {ex.Message}";
                _streamNetworkFinished = true;
            }
        }, ct);
    }

    private void OnAiError(string errorMessage)
    {
        if (!IsInstanceValid(this))
            return;

        _lastPlayerAnalysis = null;
        SetAiBusy(false);
        CommitChatLine($"[color=red]{EscapeBbcode(errorMessage)}[/color]");
    }

    private void OnAiCompleted(string rawAnswer)
    {
        if (!IsInstanceValid(this))
            return;

        SetAiBusy(false);

        if (string.IsNullOrWhiteSpace(rawAnswer))
        {
            _pendingActionSteps.Clear();
            _lastPlayerAnalysis = null;
            CommitChatLine("[color=orange]DM boş cevap döndürdü. Tekrar deneyebilirsin.[/color]");
            return;
        }

        bool isCombat = _combatUI != null && _combatUI.IsCombatActive;
        AIDMParseResult directives = AIDMDirectiveParser.Parse(rawAnswer);

        bool narrativeOverridden = ActionGatekeeper.EnforcePostRules(
            _lastPlayerAnalysis,
            isCombat,
            directives,
            _memory.SceneEntities,
            DataManager.Instance?.GetAllMonsterIds(),
            _player?.CurrentHP ?? 1,
            _player?.MaximumHP ?? 1
        );

        string cleanNarrative = directives.CleanNarrative ?? "";
        _conversationHistory.Add(new Dictionary<string, string>
        {
            { "role", "assistant" },
            { "content", cleanNarrative }
        });
        if (!string.IsNullOrWhiteSpace(cleanNarrative))
            AdventureLogger.AppendDm(cleanNarrative);

        string dmHeaderTag = _currentTotalSteps > 1
            ? $"[b]DM (Adım {_currentStepIndex}/{_currentTotalSteps}):[/b] "
            : "[b]DM:[/b] ";

        if (!string.IsNullOrEmpty(cleanNarrative))
            CommitChatLine($"{dmHeaderTag}{EscapeBbcode(cleanNarrative)}");
        else
            _chat.Text = _committedChat.ToString();

        ApplyDirectives(directives);

        bool questAdded = CampaignCanon.AutoExtractTurnMemory(_lastRawPlayerText, cleanNarrative, _memory);
        if (questAdded)
        {
            CommitChatLine($"[color=gold]📜 Görev Güncellendi: {EscapeBbcode(CampaignCanon.QuestSilverMine)}[/color]");
        }
        RefreshMemoryView();

        _lastPlayerAnalysis = null;
        ArchiveOverflowHistoryIfNeeded();
        SaveCampaignState();

        bool combatActive = _combatUI != null && _combatUI.IsCombatActive;
        bool hasPendingRoll = _pendingRoll != null;

        if (_pendingActionSteps.Count > 0)
        {
            if (combatActive || hasPendingRoll)
            {
                _pendingActionSteps.Clear();
            }
            else
            {
                StartNextSequentialStep();
            }
        }
    }

    private void ApplyDirectives(AIDMParseResult directives)
    {
        foreach (string mem in directives.Memories)
            _memory.AddFact(mem);

        string effectiveLocation = directives.LocationChange;
        if (string.IsNullOrWhiteSpace(effectiveLocation))
            effectiveLocation = ActionGatekeeper.InferLocationTransition(_memory.CurrentLocation, directives.CleanNarrative);

        bool locationChanged = !string.IsNullOrWhiteSpace(effectiveLocation) &&
                               !string.Equals(effectiveLocation, _memory.CurrentLocation, StringComparison.OrdinalIgnoreCase);

        if (locationChanged)
        {
            _memory.CurrentLocation = effectiveLocation;
            _memory.FocusedEntity = "";
            _memory.AddEvent($"Konum değişti: {effectiveLocation}");
        }

        if (!string.IsNullOrWhiteSpace(directives.SceneUpdate))
            _memory.SceneEntities = directives.SceneUpdate.Trim();
        else if (locationChanged)
            _memory.SceneEntities = ActionGatekeeper.PruneStaleSceneEntities(_memory.SceneEntities, directives.CleanNarrative);

        foreach (string q in directives.AddQuests)
        {
            _memory.AddQuest(q);
            CommitChatLine($"[color=gold]📜 Yeni Görev: {EscapeBbcode(q)}[/color]");
        }

        foreach (string cq in directives.CompleteQuests)
        {
            _memory.CompleteQuest(cq);
            CommitChatLine($"[color=green]✓ Görev Tamamlandı: {EscapeBbcode(cq)}[/color]");
        }

        if (_player != null)
        {
            if (directives.GoldDelta != 0)
            {
                _player.Gold = Math.Max(0, _player.Gold + directives.GoldDelta);
                string sign = directives.GoldDelta > 0 ? "+" : "";
                CommitChatLine($"[color=gold]🪙 Altın: {sign}{directives.GoldDelta} GP (Toplam: {_player.Gold} GP)[/color]");
            }

            foreach (string addItem in directives.AddItems)
            {
                _player.AddItem(addItem, 1);
                string nm = DataManager.Instance?.GetItemData(addItem)?.Name ?? FormatLabel(addItem);
                CommitChatLine($"[color=cyan]🎒 Eşya kazanıldı: {EscapeBbcode(nm)}[/color]");
            }

            foreach (string remItem in directives.RemoveItems)
            {
                if (_player.ConsumeItem(remItem, 1))
                {
                    string nm = DataManager.Instance?.GetItemData(remItem)?.Name ?? FormatLabel(remItem);
                    CommitChatLine($"[color=gray]🎒 Eşya kullanıldı/verildi: {EscapeBbcode(nm)}[/color]");
                }
            }
            foreach (string addCond in directives.AddConditions)
            {
                if (DataManager.Instance?.GetConditionData(addCond) == null)
                    continue;

                if (!_player.ActiveConditions.Contains(addCond))
                {
                    _player.ActiveConditions.Add(addCond);
                    _memory.AddEvent($"{_player.Name} durum kazandı: {FormatLabel(addCond)}");
                    CommitChatLine($"[color=orange]⚠ Durum eklendi: {FormatLabel(addCond)}[/color]");
                }
            }

            foreach (string remCond in directives.RemoveConditions)
            {
                if (_player.ActiveConditions.Remove(remCond))
                {
                    _memory.AddEvent($"{_player.Name} durumundan kurtuldu: {FormatLabel(remCond)}");
                    CommitChatLine($"[color=green]✓ Durum kaldırıldı: {FormatLabel(remCond)}[/color]");
                }
            }

            if (directives.DamageAmount > 0)
            {
                _player.CurrentHP = Math.Max(0, _player.CurrentHP - directives.DamageAmount);
                _memory.AddEvent($"{_player.Name} {directives.DamageAmount} hasar aldı.");
                CommitChatLine($"[color=red]💔 {EscapeBbcode(_player.Name)} {directives.DamageAmount} hasar aldı! (HP: {_player.CurrentHP}/{_player.MaximumHP})[/color]");
            }

            if (directives.HealAmount > 0)
            {
                _player.CurrentHP = Math.Min(_player.MaximumHP, _player.CurrentHP + directives.HealAmount);
                _player.DeathSaveSuccesses = 0;
                _player.DeathSaveFailures = 0;
                _memory.AddEvent($"{_player.Name} {directives.HealAmount} HP iyileşti.");
                CommitChatLine($"[color=green]💚 {EscapeBbcode(_player.Name)} {directives.HealAmount} HP iyileşti! (HP: {_player.CurrentHP}/{_player.MaximumHP})[/color]");
            }

            if (directives.RestType == "short" || directives.RestType == "long")
                ExecuteRest(directives.RestType == "long", notifyAi: false);

            RefreshCharacterSummary();
        }

        if (directives.RollRequest != null)
            ShowPendingRoll(directives.RollRequest);

        if (directives.CombatEnemies.Count > 0)
            StartCombatFromIds(directives.CombatEnemies);
    }

    private void ShowPendingRoll(PendingRollRequest request)
    {
        _pendingRoll = request;
        string kind = request.IsSavingThrow ? "Kurtarma Zarı" : "Beceri Kontrolü";
        string label = FormatLabel(request.TargetName);

        _pendingRollLabel.Text = $"DM zar istedi: {label} ({kind}, DC {request.DifficultyClass})";
        _pendingRollButton.Text = $"🎲 {label} Zarı At";
        _pendingRollBar.Visible = true;
    }

    private void ClearPendingRoll()
    {
        _pendingRoll = null;
        _pendingRollBar.Visible = false;
    }

    private void SetAiBusy(bool busy)
    {
        _isWaitingForAi = busy;
        _sendButton.Disabled = busy;
        _input.Editable = !busy;
        _quickRollButton.Disabled = busy;
        _pendingRollButton.Disabled = busy;
        _resetCampaignButton.Disabled = busy;
        _newCharacterButton.Disabled = busy;
        _shortRestButton.Disabled = busy;
        _longRestButton.Disabled = busy;
        _deathSaveButton.Disabled = busy;
    }

    private static string FormatLabel(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "";

        string[] words = raw.Replace('_', ' ').Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < words.Length; i++)
            words[i] = char.ToUpperInvariant(words[i][0]) + words[i].Substring(1).ToLowerInvariant();

        return string.Join(" ", words);
    }
}
