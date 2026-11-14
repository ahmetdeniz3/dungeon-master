using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

public partial class CombatUI : Control
{
    public event Action<bool, CombatEncounter> CombatEnded;
    public event Action EquipmentChanged;
    public event Action<string> StoryNarrativeEvent;

    private Label _playerHP;
    private Label _monsterHP;
    private Label _turnLabel;
    private Label _movementLabel;
    private Label _actionStatusLabel;

    private Button _attackButton;
    private Button _dodgeButton;
    private Button _endTurnButton;
    private Button _moveButton;
    private Button _retreatButton;
    private Button _potionButton;
    private Button _bonusActionButton;
    private Button _reactionButton;
    private Button _fleeButton;
    private Button _shieldButton;
    private Button _castSpellButton;
    private HBoxContainer _spellRow;
    private OptionButton _armorSelector;
    private OptionButton _weaponSelector;
    private OptionButton _targetSelector;
    private OptionButton _spellSelector;

    private RichTextLabel _combatLog;
    private CombatMapView _combatMap;
    private CombatEncounter _encounter;
    private CharacterData _standbyPlayer;
    private readonly DmOrchestrator _dmOrchestrator = new();
    private CancellationTokenSource _turnPipelineCts;
    private int _encounterGeneration;

    public bool IsCombatActive => _encounter != null && _encounter.IsCombatActive;
    public CombatEncounter CurrentEncounter => _encounter;

    public override void _ExitTree()
    {
        ResetTurnPipeline();
    }

    private CancellationToken ResetTurnPipeline()
    {
        _encounterGeneration++;
        _turnPipelineCts?.Cancel();
        _turnPipelineCts?.Dispose();
        _turnPipelineCts = new CancellationTokenSource();
        return _turnPipelineCts.Token;
    }

    public override void _Ready()
    {
        _playerHP = GetNodeOrNull<Label>("Margin/VBox/PlayerHP") ?? GetNodeOrNull<Label>("Margin/VBox/StatusRow/PlayerHP") ?? GetNode<Label>("PlayerHP");
        _monsterHP = GetNodeOrNull<Label>("Margin/VBox/MonsterHP") ?? GetNodeOrNull<Label>("Margin/VBox/StatusRow/MonsterHP") ?? GetNode<Label>("MonsterHP");
        _turnLabel = GetNodeOrNull<Label>("Margin/VBox/TurnInfoRow/TurnLabel") ?? GetNode<Label>("TurnLabel");
        _movementLabel = GetNodeOrNull<Label>("Margin/VBox/TurnInfoRow/MovementLabel") ?? GetNode<Label>("MovementLabel");
        _actionStatusLabel = GetNodeOrNull<Label>("Margin/VBox/ActionStatusLabel");

        _attackButton = GetNodeOrNull<Button>("Margin/VBox/ActionRow1/AttackButton") ?? GetNode<Button>("AttackButton");
        _dodgeButton = GetNodeOrNull<Button>("Margin/VBox/ActionRow1/DodgeButton");
        _endTurnButton = GetNodeOrNull<Button>("Margin/VBox/ActionRow1/EndTurnButton") ?? GetNode<Button>("EndTurnButton");
        _moveButton = GetNodeOrNull<Button>("Margin/VBox/MoveRow/MoveButton") ?? GetNode<Button>("MoveButton");
        _retreatButton = GetNodeOrNull<Button>("Margin/VBox/MoveRow/RetreatButton");
        _potionButton = GetNodeOrNull<Button>("Margin/VBox/MoveRow/PotionButton");
        _bonusActionButton = GetNodeOrNull<Button>("Margin/VBox/ActionRow2/BonusActionButton") ?? GetNode<Button>("BonusActionButton");
        _reactionButton = GetNodeOrNull<Button>("Margin/VBox/ActionRow2/ReactionButton") ?? GetNode<Button>("ReactionButton");
        _fleeButton = GetNodeOrNull<Button>("Margin/VBox/ActionRow2/FleeButton");
        _shieldButton = GetNodeOrNull<Button>("Margin/VBox/EquipRow/ShieldButton");
        _spellRow = GetNodeOrNull<HBoxContainer>("Margin/VBox/SpellRow");
        _spellSelector = GetNodeOrNull<OptionButton>("Margin/VBox/SpellRow/SpellSelector");
        _castSpellButton = GetNodeOrNull<Button>("Margin/VBox/SpellRow/CastSpellButton");

        _weaponSelector = GetNodeOrNull<OptionButton>("Margin/VBox/EquipRow/WeaponSelector") ?? GetNode<OptionButton>("WeaponSelector");
        _armorSelector = GetNodeOrNull<OptionButton>("Margin/VBox/EquipRow/ArmorSelector") ?? GetNode<OptionButton>("ArmorSelector");
        _targetSelector = GetNodeOrNull<OptionButton>("Margin/VBox/TargetSelector") ?? GetNodeOrNull<OptionButton>("TargetSelector");
        _combatLog = GetNodeOrNull<RichTextLabel>("Margin/VBox/CombatLog") ?? GetNode<RichTextLabel>("CombatLog");
        _combatMap = GetNodeOrNull<CombatMapView>("Margin/VBox/CombatMap");

        _attackButton.Pressed += OnAttackPressed;
        if (_dodgeButton != null) _dodgeButton.Pressed += OnDodgePressed;
        _moveButton.Pressed += OnMoveCloserPressed;
        if (_retreatButton != null) _retreatButton.Pressed += OnMoveAwayPressed;
        if (_potionButton != null) _potionButton.Pressed += OnPotionPressed;
        _endTurnButton.Pressed += OnEndTurnPressed;
        _bonusActionButton.Pressed += OnBonusActionPressed;
        _reactionButton.Pressed += OnReactionPressed;
        if (_fleeButton != null) _fleeButton.Pressed += OnFleePressed;
        if (_shieldButton != null) _shieldButton.Pressed += OnToggleShieldPressed;
        if (_castSpellButton != null) _castSpellButton.Pressed += OnCastSpellPressed;

        _weaponSelector.ItemSelected += OnWeaponSelected;
        _armorSelector.ItemSelected += OnArmorSelected;
        if (_targetSelector != null)
            _targetSelector.ItemSelected += _ => UpdateUI();

        SetStandbyState();
    }

    public void SetStandbyState(CharacterData player = null)
    {
        ResetTurnPipeline();
        _encounter = null;
        _standbyPlayer = player;
        _playerHP.Text = player != null
            ? $"{player.Name} HP: {player.CurrentHP}/{player.MaximumHP} | AC: {player.ArmorClass}"
            : "Oyuncu HP: -";
        _monsterHP.Text = "Düşman: Yok";
        _turnLabel.Text = "Aktif savaş yok";
        _movementLabel.Text = "Mesafe: -";
        if (_actionStatusLabel != null)
            _actionStatusLabel.Text = "Durum: Savaş bekleniyor";
        _combatMap?.UpdateMap(null);

        _attackButton.Disabled = true;
        if (_dodgeButton != null) _dodgeButton.Disabled = true;
        _moveButton.Disabled = true;
        if (_retreatButton != null) _retreatButton.Disabled = true;
        _bonusActionButton.Disabled = true;
        _reactionButton.Disabled = true;
        _endTurnButton.Disabled = true;
        if (_fleeButton != null) _fleeButton.Disabled = true;
        if (_castSpellButton != null) _castSpellButton.Disabled = true;

        _targetSelector?.Clear();
        if (player != null)
        {
            SetupWeaponSelector(player);
            SetupArmorSelector(player);
            SetupSpellSelector(player);
            UpdateShieldAndPotionLabels(player, true);
        }
    }

    public void StartEncounter(CharacterData player, List<CombatMonster> monsters)
    {
        if (player == null || monsters == null || monsters.Count == 0)
            return;

        ResetTurnPipeline();
        _standbyPlayer = player;
        _encounter = new CombatEncounter(player, monsters);
        _encounter.StartCombat();

        SetupWeaponSelector(player);
        SetupArmorSelector(player);
        SetupSpellSelector(player);
        RefreshTargetSelector();

        _combatLog?.Clear();
        AddLog($"⚔ Savaş başladı! Mesafe: {_encounter.EngagementDistance} ft ({string.Join(", ", monsters.Select(m => m.DisplayName))})");

        UpdateUI();
        ProcessCurrentTurn();
    }

    public void AddEnemies(List<CombatMonster> newMonsters)
    {
        if (_encounter == null || !_encounter.IsCombatActive || newMonsters == null || newMonsters.Count == 0)
            return;

        _encounter.AddReinforcements(newMonsters);
        RefreshTargetSelector();
        AddLog($"⚔ Takviye düşmanlar katıldı: {string.Join(", ", newMonsters.Select(m => m.DisplayName))}");
        UpdateUI();
    }

    private void ProcessCurrentTurn()
    {
        if (_encounter == null || !_encounter.IsCombatActive)
        {
            UpdateUI();
            return;
        }

        DmTurnDecision decision = _dmOrchestrator.DecideTurnStart(_encounter);
        if (decision == null)
            return;

        if (decision.IsPlayer)
        {
            HandlePlayerTurnStart(decision);
            return;
        }

        ProcessMonsterTurn(decision);
    }

    private async void HandlePlayerTurnStart(DmTurnDecision decision)
    {
        DmPlayerCheckpoint checkpoint = _dmOrchestrator.EvaluatePlayerCheckpoint(_encounter.Player);
        AddLog(checkpoint.SummaryLine);

        if (checkpoint.CanAct)
        {
            UpdateUI();
            return;
        }

        UpdateUI();
        int gen = _encounterGeneration;
        CancellationToken ct = _turnPipelineCts?.Token ?? CancellationToken.None;
        StoryNarrativeEvent?.Invoke($"{_encounter.Player.Name} aksiyon alamıyor ({checkpoint.BlockingReason}); DM turu sıradaki NPC'ye aktarıyor.");

        try
        {
            await ToSignal(GetTree().CreateTimer(0.9), SceneTreeTimer.SignalName.Timeout);
        }
        catch
        {
            return;
        }

        if (ct.IsCancellationRequested || gen != _encounterGeneration || !IsInstanceValid(this) || _encounter == null || !_encounter.IsCombatActive)
            return;

        _encounter.NextTurn();
        UpdateUI();
        ProcessCurrentTurn();
    }

    private CombatMonster GetSelectedTarget()
    {
        if (_encounter == null)
            return null;

        if (_targetSelector != null && _targetSelector.Selected >= 0 && _targetSelector.ItemCount > 0)
        {
            string instanceId = _targetSelector.GetItemMetadata(_targetSelector.Selected).AsString();
            CombatMonster selected = _encounter.GetMonsterByInstanceId(instanceId);
            if (selected != null && !selected.IsDead())
                return selected;
        }

        return _encounter.GetAliveMonsters().FirstOrDefault();
    }

    private void OnAttackPressed()
    {
        if (!ValidatePlayerTurn(out CombatTurnState state))
            return;

        if (CombatSystem.CannotAct(_encounter.Player.ActiveConditions))
        {
            AddLog("Aktif durum nedeniyle şu anda aksiyon alamıyorsun!");
            return;
        }

        if (!state.ActionAvailable)
        {
            AddLog("Bu turda Action zaten kullanıldı.");
            return;
        }

        CombatMonster target = GetSelectedTarget();
        if (target == null)
        {
            AddLog("Saldıracak hayatta düşman kalmadı.");
            return;
        }

        string weaponId = _encounter.Player.EquippedWeaponId;
        WeaponData weapon = DataManager.Instance?.GetWeaponData(weaponId);
        bool isRanged = weapon?.Category != null && weapon.Category.Contains("ranged", StringComparison.OrdinalIgnoreCase);

        if (!isRanged && _encounter.EngagementDistance > 5)
        {
            AddLog($"Hedef {_encounter.EngagementDistance} ft uzakta! Yakın dövüş için 'Yaklaş' butonuna bas veya menzilli silah/büyü seç.");
            return;
        }

        CombatAction action = _encounter.PerformWeaponAttack(weaponId, target);
        if (action == null || action.AttackResult == null)
        {
            AddLog("Saldırı gerçekleştirilemedi.");
            return;
        }

        CombatAttackResult result = action.AttackResult;
        string playerName = _encounter.Player.Name;
        string weaponName = weapon?.Name ?? weaponId;

        if (result.Hit)
        {
            string critTag = result.Critical ? " [KRİTİK!]" : "";
            string masteryNote = "";
            if (!result.TargetDead && weapon != null && !string.IsNullOrEmpty(weapon.Mastery))
            {
                if (weapon.Mastery.Equals("Sap", StringComparison.OrdinalIgnoreCase))
                    masteryNote = " [Sap: Düşmanın sıradaki saldırısı dezavantajlı]";
                else if (weapon.Mastery.Equals("Vex", StringComparison.OrdinalIgnoreCase))
                    masteryNote = " [Vex: Sıradaki saldırın avantajlı]";
            }

            AddLog(
                $"{playerName}, {weaponName} ile {target.DisplayName}'e vurdu!{critTag} " +
                $"Atış: {result.Attack.DiceRoll} + {result.Attack.Modifier} = {result.Attack.Total} | Hasar: {result.DamageDealt}{masteryNote}"
            );
        }
        else
        {
            AddLog(
                $"{playerName}, {weaponName} ile {target.DisplayName}'e saldırdı ama ıskaladı! " +
                $"Atış: {result.Attack.DiceRoll} + {result.Attack.Modifier} = {result.Attack.Total}"
            );
        }

        if (result.TargetDead)
        {
            AddLog($"💀 {target.DisplayName} öldü!");
            RefreshTargetSelector();
        }

        UpdateUI();

        if (!_encounter.IsCombatActive)
        {
            AddLog("🏆 Zafer! Tüm düşmanlar yenildi.");
            CombatEnded?.Invoke(true, _encounter);
        }
    }

    private void OnDodgePressed()
    {
        if (!ValidatePlayerTurn(out CombatTurnState state))
            return;

        if (!state.ActionAvailable || !_encounter.PerformDodge())
        {
            AddLog("Bu turda Action kullanılamıyor.");
            return;
        }

        AddLog($"🛡 {_encounter.Player.Name} savunmaya geçti (Dodge): Sıradaki tura kadar gelen saldırılar dezavantajlı!");
        UpdateUI();
    }

    private void OnFleePressed()
    {
        if (!ValidatePlayerTurn(out CombatTurnState state))
            return;

        if (!state.ActionAvailable)
        {
            AddLog("Kaçmak için Action gerekli.");
            return;
        }

        bool escaped = _encounter.AttemptFlee(out CheckResult roll);
        string rollText = roll != null ? $"(d20:{roll.DiceRoll} + {roll.Modifier} = {roll.Total} vs DC 12)" : "";

        if (escaped)
        {
            AddLog($"🏃 {_encounter.Player.Name} savaştan başarıyla kaçtı! {rollText}");
            UpdateUI();
            CombatEnded?.Invoke(false, _encounter);
        }
        else
        {
            AddLog($"🏃 Kaçış başarısız! {rollText} Düşmanlar yolunu kesiyor.");
            UpdateUI();
        }
    }

    private void OnPotionPressed()
    {
        CharacterData player = _encounter?.Player ?? _standbyPlayer;
        if (player == null)
            return;

        if (player.GetItemCount("healing_potion") <= 0)
        {
            AddLog("Çantanda şifa iksiri kalmadı.");
            return;
        }

        if (_encounter != null && _encounter.IsCombatActive)
        {
            if (!ValidatePlayerTurn(out _))
                return;

            if (!_encounter.UseHealingPotion(out int healed))
            {
                AddLog("İksir içmek için Action gerekli.");
                return;
            }

            AddLog($"🧪 {_encounter.Player.Name} Şifa İksiri içti (+{healed} HP -> {_encounter.Player.CurrentHP}/{_encounter.Player.MaximumHP})!");
            EquipmentChanged?.Invoke();
            UpdateUI();
            return;
        }

        if (player.CurrentHP >= player.MaximumHP)
        {
            AddLog("HP zaten dolu.");
            return;
        }

        player.ConsumeItem("healing_potion", 1);
        int healAmount = Math.Max(1, DiceRoller.RollExpression("2d4+2"));
        int before = player.CurrentHP;
        player.CurrentHP = Math.Min(player.MaximumHP, player.CurrentHP + healAmount);
        AddLog($"🧪 {player.Name} Şifa İksiri içti (+{player.CurrentHP - before} HP).");
        EquipmentChanged?.Invoke();
        SetStandbyState(player);
    }

    private void OnMoveCloserPressed()
    {
        if (!ValidatePlayerTurn(out _))
            return;

        if (!_encounter.MoveCloser(10))
        {
            AddLog(_encounter.EngagementDistance <= 5 ? "Zaten yakın dövüş mesafesindesin (5 ft)." : "Yeterli hareket puanı yok.");
            return;
        }

        AddLog($"{_encounter.Player.Name} düşmana yaklaştı (Mesafe: {_encounter.EngagementDistance} ft).");
        UpdateUI();
    }

    private void OnMoveAwayPressed()
    {
        if (!ValidatePlayerTurn(out _))
            return;

        if (!_encounter.MoveAway(10))
        {
            AddLog("Uzaklaşmak için yeterli hareket puanı yok.");
            return;
        }

        AddLog($"{_encounter.Player.Name} geri çekildi (Mesafe: {_encounter.EngagementDistance} ft).");
        UpdateUI();
    }

    private void OnBonusActionPressed()
    {
        if (!ValidatePlayerTurn(out CombatTurnState state))
            return;

        if (!state.BonusActionAvailable)
        {
            AddLog("Bu turda Bonus Action kullanılamıyor.");
            return;
        }

        if (!_encounter.SecondWindUsedInEncounter &&
            string.Equals(_encounter.Player.ClassId, "fighter", StringComparison.OrdinalIgnoreCase) &&
            _encounter.Player.CurrentHP < _encounter.Player.MaximumHP)
        {
            int healed = _encounter.UseSecondWind();
            AddLog($"💚 Second Wind (Bonus Action): {_encounter.Player.Name} +{healed} HP iyileşti! ({_encounter.Player.CurrentHP}/{_encounter.Player.MaximumHP})");
            EquipmentChanged?.Invoke();
            UpdateUI();
            return;
        }

        if (!_encounter.UseCurrentBonusAction())
        {
            AddLog("Bonus Action kullanılamıyor.");
            return;
        }

        AddLog($"{_encounter.Player.Name} Bonus Action kullandı (Not: Saldırı aksiyonunu yenilemez, sıradaki tur için 'Turu Bitir'e bas).");
        UpdateUI();
    }

    private void OnReactionPressed()
    {
        if (!ValidatePlayerTurn(out CombatTurnState state))
            return;

        if (!state.ReactionAvailable || !_encounter.UseCurrentReaction())
        {
            AddLog("Bu turda Reaction kullanılamıyor.");
            return;
        }

        AddLog($"{_encounter.Player.Name} Reaction kullandı.");
        UpdateUI();
    }

    private void OnEndTurnPressed()
    {
        if (!ValidatePlayerTurn(out _))
            return;

        AddLog($"{_encounter.Player.Name} turunu bitirdi.");
        _encounter.EndTurn();
        UpdateUI();
        ProcessCurrentTurn();
    }

    private void OnToggleShieldPressed()
    {
        CharacterData player = _encounter?.Player ?? _standbyPlayer;
        if (player == null || !player.HasShieldInInventory)
            return;

        player.EquippedShieldId = string.IsNullOrEmpty(player.EquippedShieldId) ? "shield" : "";
        ArmorData armor = !string.IsNullOrEmpty(player.EquippedArmorId)
            ? DataManager.Instance?.GetArmorData(player.EquippedArmorId)
            : null;
        player.ArmorClass = ArmorClassCalculator.Calculate(player, armor);

        string stateText = string.IsNullOrEmpty(player.EquippedShieldId) ? "çıkarıldı" : "kuşanıldı (+2 AC)";
        AddLog($"🛡 Kalkan {stateText}. Yeni AC: {player.ArmorClass}");
        EquipmentChanged?.Invoke();

        if (_encounter != null)
            UpdateUI();
        else
            SetStandbyState(player);
    }

    private bool ValidatePlayerTurn(out CombatTurnState state)
    {
        state = null;
        if (_encounter == null || !_encounter.IsCombatActive)
            return false;

        InitiativeResult current = _encounter.GetCurrentCombatant();
        if (current == null || !current.IsPlayer)
        {
            AddLog("Şu anda senin turun değil.");
            return false;
        }

        state = _encounter.GetCurrentTurnState();
        return state != null;
    }

    private async void ProcessMonsterTurn(DmTurnDecision dmDecision)
    {
        if (_encounter == null || !_encounter.IsCombatActive)
            return;

        InitiativeResult current = _encounter.GetCurrentCombatant();
        if (current == null || current.IsPlayer)
            return;

        CombatMonster monster = _encounter.GetMonsterByInstanceId(current.CombatantId);
        if (monster == null || monster.IsDead() || _encounter.Player.CurrentHP <= 0)
        {
            _encounter.NextTurn();
            ProcessCurrentTurn();
            return;
        }

        int gen = _encounterGeneration;
        CancellationToken ct = _turnPipelineCts?.Token ?? CancellationToken.None;

        // 1. DM Düşünür & Karar Verir
        UpdateUI();
        _turnLabel.Text = $"Tur {_encounter.RoundNumber}: {monster.DisplayName}";
        if (_actionStatusLabel != null)
            _actionStatusLabel.Text = $"DM Kararı -> {monster.DisplayName} ({dmDecision.PlannedActionLabel})";
        AddLog(dmDecision.SummaryLine);

        // 2. NPC Ajanı Düşünür & Oynar
        if (_actionStatusLabel != null)
            _actionStatusLabel.Text = $"{monster.DisplayName} Ajanı rolünü oynuyor...";

        NpcAgentPlayResult npcPlay = await _dmOrchestrator.NpcAgent.PlayTurnAsync(monster, _encounter, dmDecision, ct);
        if (ct.IsCancellationRequested || gen != _encounterGeneration || !IsInstanceValid(this) || _encounter == null || !_encounter.IsCombatActive)
            return;

        current = _encounter.GetCurrentCombatant();
        if (current == null || current.CombatantId != monster.InstanceId)
            return;

        AddLog($"{npcPlay.PersonaTag} [{npcPlay.AgentName} Ajanı] {npcPlay.RoleplayLine}");

        if (npcPlay.ChosenIntent == CombatAgentIntent.CannotAct || CombatSystem.CannotAct(monster.ActiveConditions))
        {
            AddLog($"{monster.DisplayName} etkisiz durumda ve aksiyon alamıyor!");
            StoryNarrativeEvent?.Invoke($"[{npcPlay.AgentName}] {npcPlay.RoleplayLine}");
            _encounter.NextTurn();
            UpdateUI();
            ProcessCurrentTurn();
            return;
        }

        // 3. Mekanik Çözüm + DM Sonucu Hesaplar & Yorumlar
        if (_actionStatusLabel != null)
            _actionStatusLabel.Text = "DM zarı/mekaniği hesaplayıp yorumluyor...";

        int prevDist = _encounter.EngagementDistance;
        CombatAction action = _encounter.PerformMonsterAttack(monster, _encounter.Player);

        DmResolutionResult resolution = await _dmOrchestrator.ResolveAndCommentAsync(
            _encounter,
            monster,
            npcPlay,
            action,
            prevDist,
            ct
        );

        if (ct.IsCancellationRequested || gen != _encounterGeneration || !IsInstanceValid(this) || _encounter == null)
            return;

        if (_encounter.EngagementDistance < prevDist)
            AddLog($"👣 {monster.DisplayName} yaklaştı (Mesafe: {_encounter.EngagementDistance} ft).");

        AddLog(resolution.MechanicsLine);
        AddLog($"🎭 [DM Yorum] {resolution.DmNarrative}");

        StoryNarrativeEvent?.Invoke($"{npcPlay.AgentName}: {npcPlay.RoleplayLine} — {resolution.DmNarrative}");

        // 4. Kontrol Noktası: Oyuncu 0 HP'ye düştü mü?
        if (action?.AttackResult?.TargetDead == true || _encounter.Player.CurrentHP <= 0)
        {
            AddLog(resolution.Checkpoint.SummaryLine);
            UpdateUI();
            CombatEnded?.Invoke(false, _encounter);
            return;
        }

        UpdateUI();
        ProcessCurrentTurn();
    }

    private void UpdateUI()
    {
        if (_encounter == null)
            return;

        CharacterData player = _encounter.Player;
        _playerHP.Text = $"{player.Name} HP: {player.CurrentHP}/{player.MaximumHP} | AC: {player.ArmorClass}";

        var aliveMonsters = _encounter.GetAliveMonsters();
        if (aliveMonsters.Count > 0)
        {
            _monsterHP.Text = "Düşmanlar: " + string.Join(" | ", aliveMonsters.Select(m => $"{m.DisplayName} ({m.CurrentHP}/{m.MaximumHP})"));
        }
        else
        {
            _monsterHP.Text = "Düşmanlar: Tümü yenildi";
        }

        _combatMap?.UpdateMap(_encounter, GetSelectedTarget()?.InstanceId);

        if (!_encounter.IsCombatActive)
        {
            _turnLabel.Text = _encounter.PlayerFled
                ? "Savaş Bitti (Kaçıldı)"
                : (player.CurrentHP <= 0 ? "Savaş Bitti (Yenilgi)" : "Savaş Bitti (Zafer)");
            _movementLabel.Text = $"Mesafe: {_encounter.EngagementDistance} ft";
            if (_actionStatusLabel != null)
                _actionStatusLabel.Text = "Savaş sona erdi.";
            _attackButton.Disabled = true;
            if (_dodgeButton != null) _dodgeButton.Disabled = true;
            _moveButton.Disabled = true;
            if (_retreatButton != null) _retreatButton.Disabled = true;
            _bonusActionButton.Disabled = true;
            _reactionButton.Disabled = true;
            _endTurnButton.Disabled = true;
            if (_fleeButton != null) _fleeButton.Disabled = true;
            if (_castSpellButton != null) _castSpellButton.Disabled = true;
            UpdateShieldAndPotionLabels(player, false);
            return;
        }

        InitiativeResult current = _encounter.GetCurrentCombatant();
        CombatTurnState state = _encounter.GetCurrentTurnState();
        if (current == null || state == null)
            return;

        string rangeTag = _encounter.EngagementDistance <= 5 ? "Yakın" : "Uzak";
        _turnLabel.Text = $"Tur {_encounter.RoundNumber}: {current.CombatantName}";
        _movementLabel.Text = $"Mesafe: {_encounter.EngagementDistance} ft ({rangeTag}) | Hız: {state.MovementRemaining} ft";

        bool playerTurn = current.IsPlayer;
        if (_actionStatusLabel != null)
        {
            if (!playerTurn)
            {
                _actionStatusLabel.Text = $"Sıra {current.CombatantName}'de...";
            }
            else if (state.ActionAvailable)
            {
                string baText = state.BonusActionAvailable ? "Hazır ✓" : "Kullanıldı";
                _actionStatusLabel.Text = $"Action: Hazır ✓ | Bonus Action: {baText}";
            }
            else
            {
                string baText = state.BonusActionAvailable ? "Hazır ✓" : "Kullanıldı";
                _actionStatusLabel.Text = $"Action kullanıldı (BA: {baText}) → Sıradaki tur için 'Turu Bitir'e bas!";
            }
        }
        _attackButton.Disabled = !playerTurn || !state.ActionAvailable || _encounter.AreAllMonstersDead();
        if (_dodgeButton != null) _dodgeButton.Disabled = !playerTurn || !state.ActionAvailable;
        _moveButton.Disabled = !playerTurn || _encounter.EngagementDistance <= 5 || state.MovementRemaining <= 0;
        if (_retreatButton != null) _retreatButton.Disabled = !playerTurn || state.MovementRemaining < 10 || _encounter.EngagementDistance >= 60;
        _bonusActionButton.Disabled = !playerTurn || !state.BonusActionAvailable;
        _bonusActionButton.Text = (!_encounter.SecondWindUsedInEncounter && string.Equals(player.ClassId, "fighter", StringComparison.OrdinalIgnoreCase))
            ? "Second Wind (BA)"
            : "Bonus Action";
        _reactionButton.Disabled = !playerTurn || !state.ReactionAvailable;
        _endTurnButton.Disabled = !playerTurn;
        if (_fleeButton != null) _fleeButton.Disabled = !playerTurn || !state.ActionAvailable;
        if (_castSpellButton != null)
            _castSpellButton.Disabled = !playerTurn || !state.ActionAvailable || _spellSelector == null || _spellSelector.ItemCount == 0;

        UpdateShieldAndPotionLabels(player, playerTurn && state.ActionAvailable);
    }

    private void UpdateShieldAndPotionLabels(CharacterData player, bool canUsePotionAction)
    {
        if (_shieldButton != null)
        {
            _shieldButton.Visible = player.HasShieldInInventory;
            _shieldButton.Text = string.IsNullOrEmpty(player.EquippedShieldId)
                ? "🛡 Kalkan (+)"
                : "🛡 Kalkan (-)";
        }

        if (_potionButton != null)
        {
            int count = player.GetItemCount("healing_potion");
            _potionButton.Text = $"🧪 İksir ({count})";
            _potionButton.Disabled = count <= 0 || (_encounter != null && _encounter.IsCombatActive && !canUsePotionAction);
        }
    }

    private void SetupSpellSelector(CharacterData player)
    {
        if (_spellRow == null || _spellSelector == null)
            return;

        _spellSelector.Clear();
        if (player == null || player.KnownSpells == null || player.KnownSpells.Count == 0 || DataManager.Instance == null)
        {
            _spellRow.Visible = false;
            return;
        }

        foreach (string spellId in player.KnownSpells)
        {
            SpellData spell = DataManager.Instance.GetSpellData(spellId);
            if (spell == null)
                continue;

            string slotInfo = spell.Level == 0
                ? "Cantrip"
                : $"Sv.{spell.Level} ({(player.SpellSlotsCurrent.TryGetValue(spell.Level, out int cur) ? cur : 0)} slot)";
            _spellSelector.AddItem($"{spell.Name} [{slotInfo}]");
            _spellSelector.SetItemMetadata(_spellSelector.ItemCount - 1, spell.Id);
        }

        _spellRow.Visible = _spellSelector.ItemCount > 0;
    }

    private void OnCastSpellPressed()
    {
        if (!ValidatePlayerTurn(out CombatTurnState state) || _spellSelector == null || _spellSelector.Selected < 0)
            return;

        if (!state.ActionAvailable)
        {
            AddLog("Bu turda Action zaten kullanıldı.");
            return;
        }

        string spellId = _spellSelector.GetItemMetadata(_spellSelector.Selected).AsString();
        SpellData spell = DataManager.Instance?.GetSpellData(spellId);
        if (spell == null)
            return;

        CombatMonster target = string.IsNullOrEmpty(spell.HealingDice) ? GetSelectedTarget() : null;
        SpellCastResult result = _encounter.PerformSpellCast(spellId, target);
        if (result == null)
        {
            AddLog($"{spell.Name} yapılamadı (yetersiz spell slot veya hedef yok).");
            return;
        }

        AddLog(result.Summary);
        if (result.TargetDead && target != null)
        {
            AddLog($"💀 {target.DisplayName} öldü!");
            RefreshTargetSelector();
        }

        SetupSpellSelector(_encounter.Player);
        EquipmentChanged?.Invoke();
        UpdateUI();

        if (!_encounter.IsCombatActive)
        {
            AddLog("🏆 Zafer! Tüm düşmanlar yenildi.");
            CombatEnded?.Invoke(true, _encounter);
        }
    }

    private void AddLog(string message)
    {
        if (_combatLog == null)
            return;

        _combatLog.AppendText(message + "\n");
        CallDeferred(MethodName.ScrollLogToBottom);
    }

    private void ScrollLogToBottom()
    {
        if (_combatLog == null)
            return;

        VScrollBar scrollBar = _combatLog.GetVScrollBar();
        if (scrollBar != null)
            scrollBar.Value = scrollBar.MaxValue;
    }

    private void RefreshTargetSelector()
    {
        if (_targetSelector == null || _encounter == null)
            return;

        _targetSelector.Clear();
        foreach (CombatMonster monster in _encounter.GetAliveMonsters())
        {
            _targetSelector.AddItem($"{monster.DisplayName} ({monster.CurrentHP}/{monster.MaximumHP} HP)");
            _targetSelector.SetItemMetadata(_targetSelector.ItemCount - 1, monster.InstanceId);
        }

        if (_targetSelector.ItemCount > 0)
            _targetSelector.Select(0);
    }

    private void SetupWeaponSelector(CharacterData player)
    {
        _weaponSelector.Clear();
        if (player == null || DataManager.Instance == null)
            return;

        foreach (string weaponId in player.WeaponInventory)
        {
            WeaponData weapon = DataManager.Instance.GetWeaponData(weaponId);
            if (weapon == null)
                continue;

            string label = string.IsNullOrEmpty(weapon.Mastery) ? weapon.Name : $"{weapon.Name} [{weapon.Mastery}]";
            _weaponSelector.AddItem(label);
            _weaponSelector.SetItemMetadata(_weaponSelector.ItemCount - 1, weapon.Id);
        }

        for (int i = 0; i < _weaponSelector.ItemCount; i++)
        {
            if (_weaponSelector.GetItemMetadata(i).AsString() == player.EquippedWeaponId)
            {
                _weaponSelector.Select(i);
                break;
            }
        }
    }

    private void SetupArmorSelector(CharacterData player)
    {
        _armorSelector.Clear();
        if (player == null || DataManager.Instance == null)
            return;

        var armorIds = new List<string>(player.ArmorInventory);
        if (!string.IsNullOrEmpty(player.EquippedArmorId) && !armorIds.Contains(player.EquippedArmorId))
            armorIds.Add(player.EquippedArmorId);

        foreach (string armorId in armorIds)
        {
            ArmorData armor = DataManager.Instance.GetArmorData(armorId);
            if (armor == null)
                continue;

            _armorSelector.AddItem(armor.Name);
            _armorSelector.SetItemMetadata(_armorSelector.ItemCount - 1, armor.Id);
        }

        for (int i = 0; i < _armorSelector.ItemCount; i++)
        {
            if (_armorSelector.GetItemMetadata(i).AsString() == player.EquippedArmorId)
            {
                _armorSelector.Select(i);
                break;
            }
        }
    }

    private void OnWeaponSelected(long index)
    {
        if (DataManager.Instance == null || index < 0 || index >= _weaponSelector.ItemCount)
            return;

        CharacterData player = _encounter?.Player ?? _standbyPlayer ?? DataManager.Instance.GetCharacterData("test_character");
        if (player == null)
            return;

        string weaponId = _weaponSelector.GetItemMetadata((int)index).AsString();
        player.EquippedWeaponId = weaponId;
        AddLog($"Kuşanılan silah: {DataManager.Instance.GetWeaponData(weaponId)?.Name ?? weaponId}");
        EquipmentChanged?.Invoke();
    }

    private void OnArmorSelected(long index)
    {
        if (DataManager.Instance == null || index < 0 || index >= _armorSelector.ItemCount)
            return;

        CharacterData player = _encounter?.Player ?? _standbyPlayer ?? DataManager.Instance.GetCharacterData("test_character");
        if (player == null)
            return;

        string armorId = _armorSelector.GetItemMetadata((int)index).AsString();
        ArmorData armor = DataManager.Instance.GetArmorData(armorId);
        if (armor == null)
            return;

        player.EquippedArmorId = armorId;
        player.ArmorClass = ArmorClassCalculator.Calculate(player, armor);

        AddLog($"Kuşanılan zırh: {armor.Name} | AC: {player.ArmorClass}");
        UpdateUI();
        EquipmentChanged?.Invoke();
    }
}
