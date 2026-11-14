using System;
using System.Collections.Generic;
using System.Linq;

public class CombatEncounter
{
    public CharacterData Player { get; private set; }
    public List<CombatMonster> Monsters { get; private set; } = new();
    public InitiativeResult PlayerInitiative { get; private set; }
    public List<InitiativeResult> MonsterInitiatives { get; private set; } = new();
    public List<InitiativeResult> TurnOrder { get; private set; } = new();
    public int CurrentTurnIndex { get; private set; } = 0;
    public int RoundNumber { get; private set; } = 1;
    public int EngagementDistance { get; private set; } = 15;
    public bool IsCombatActive { get; private set; }
    public bool PlayerFled { get; private set; }
    public bool PlayerNextAttackAdvantage { get; private set; }
    public bool SecondWindUsedInEncounter { get; private set; }
    public Dictionary<string, CombatTurnState> TurnStates { get; private set; } = new();

    public CombatEncounter(CharacterData player, List<CombatMonster> monsters)
    {
        Player = player;
        Monsters = monsters ?? new List<CombatMonster>();
        IsCombatActive = false;
    }

    public void StartCombat()
    {
        TurnOrder.Clear();
        MonsterInitiatives.Clear();
        TurnStates.Clear();
        RoundNumber = 1;
        EngagementDistance = 15;
        PlayerFled = false;
        PlayerNextAttackAdvantage = false;
        SecondWindUsedInEncounter = false;
        Player.IsDodging = false;

        PlayerInitiative = InitiativeCalculator.RollCharacterInitiative(Player);
        TurnOrder.Add(PlayerInitiative);
        TurnStates[Player.Id] = new CombatTurnState(Player.WalkingSpeed);

        foreach (CombatMonster monster in Monsters)
        {
            InitiativeResult initiative = InitiativeCalculator.RollMonsterInitiative(monster);
            MonsterInitiatives.Add(initiative);
            TurnOrder.Add(initiative);
            TurnStates[monster.InstanceId] = new CombatTurnState(monster.Data.Combat.Speed.Walking);
        }

        TurnOrder.Sort((a, b) => b.Total.CompareTo(a.Total));

        CurrentTurnIndex = 0;
        IsCombatActive = Monsters.Count > 0 && Player.CurrentHP > 0 && !AreAllMonstersDead();

        if (IsCombatActive)
            ResetCurrentTurn();
    }

    public void AddReinforcements(List<CombatMonster> newMonsters)
    {
        if (!IsCombatActive || newMonsters == null || newMonsters.Count == 0)
            return;

        foreach (CombatMonster monster in newMonsters)
        {
            Monsters.Add(monster);
            InitiativeResult initiative = InitiativeCalculator.RollMonsterInitiative(monster);
            MonsterInitiatives.Add(initiative);
            TurnOrder.Add(initiative);
            TurnStates[monster.InstanceId] = new CombatTurnState(monster.Data.Combat.Speed.Walking);
        }
    }

    public bool AreAllMonstersDead()
    {
        return Monsters.All(m => m.IsDead());
    }

    public List<CombatMonster> GetAliveMonsters()
    {
        return Monsters.Where(m => !m.IsDead()).ToList();
    }

    public CombatMonster GetMonsterByInstanceId(string instanceId)
    {
        return Monsters.FirstOrDefault(m => m.InstanceId == instanceId);
    }

    private bool IsCombatantAlive(InitiativeResult combatant)
    {
        if (combatant == null)
            return false;

        if (combatant.IsPlayer)
            return Player.CurrentHP > 0;

        CombatMonster monster = GetMonsterByInstanceId(combatant.CombatantId);
        return monster != null && !monster.IsDead();
    }

    private void ResetCurrentTurn()
    {
        InitiativeResult current = GetCurrentCombatant();
        if (current == null || !TurnStates.TryGetValue(current.CombatantId, out CombatTurnState state))
            return;

        if (current.IsPlayer)
            Player.IsDodging = false;

        int walkingSpeed = current.IsPlayer
            ? Player.WalkingSpeed
            : (GetMonsterByInstanceId(current.CombatantId)?.Data.Combat.Speed.Walking ?? 0);

        state.Reset(walkingSpeed);
    }

    public InitiativeResult GetCurrentCombatant()
    {
        if (!IsCombatActive || TurnOrder.Count == 0)
            return null;

        return TurnOrder[CurrentTurnIndex];
    }

    public CombatTurnState GetCurrentTurnState()
    {
        InitiativeResult current = GetCurrentCombatant();
        if (current == null)
            return null;

        return TurnStates.TryGetValue(current.CombatantId, out CombatTurnState state) ? state : null;
    }

    public bool MoveCurrentCombatant(int distance)
    {
        if (!IsCombatActive)
            return false;

        CombatTurnState state = GetCurrentTurnState();
        return state != null && state.UseMovement(distance);
    }

    public bool MoveCloser(int step = 10)
    {
        if (!IsCombatActive || EngagementDistance <= 5)
            return false;

        int needed = Math.Min(step, EngagementDistance - 5);
        if (!MoveCurrentCombatant(needed))
            return false;

        EngagementDistance = Math.Max(5, EngagementDistance - needed);
        return true;
    }

    public bool MoveAway(int step = 10)
    {
        if (!IsCombatActive || EngagementDistance >= 60)
            return false;

        if (!MoveCurrentCombatant(step))
            return false;

        EngagementDistance = Math.Min(60, EngagementDistance + step);
        return true;
    }

    public bool PerformDodge()
    {
        if (!IsCombatActive)
            return false;

        InitiativeResult current = GetCurrentCombatant();
        if (current == null || !current.IsPlayer)
            return false;

        CombatTurnState state = GetCurrentTurnState();
        if (state == null || !state.UseAction())
            return false;

        Player.IsDodging = true;
        return true;
    }

    public bool AttemptFlee(out CheckResult rollResult)
    {
        rollResult = null;
        if (!IsCombatActive)
            return false;

        InitiativeResult current = GetCurrentCombatant();
        if (current == null || !current.IsPlayer)
            return false;

        CombatTurnState state = GetCurrentTurnState();
        if (state == null || !state.UseAction())
            return false;

        int athMod = SkillCalculator.GetSkillModifier(
            Player.AbilityScores.Strength,
            Player.Level,
            Player.Skills.TryGetValue("athletics", out SkillData ath) ? ath : null
        );
        int acrMod = SkillCalculator.GetSkillModifier(
            Player.AbilityScores.Dexterity,
            Player.Level,
            Player.Skills.TryGetValue("acrobatics", out SkillData acr) ? acr : null
        );

        if (acrMod > athMod)
            rollResult = SkillCheckCalculator.RollSkillCheck(Player, "acrobatics", Player.AbilityScores.Dexterity);
        else
            rollResult = SkillCheckCalculator.RollSkillCheck(Player, "athletics", Player.AbilityScores.Strength);

        if (rollResult != null && rollResult.Total >= 12)
        {
            PlayerFled = true;
            IsCombatActive = false;
            return true;
        }

        return false;
    }

    public bool UseHealingPotion(out int healedAmount)
    {
        healedAmount = 0;
        if (!IsCombatActive)
            return false;

        InitiativeResult current = GetCurrentCombatant();
        if (current == null || !current.IsPlayer)
            return false;

        if (Player.GetItemCount("healing_potion") <= 0)
            return false;

        CombatTurnState state = GetCurrentTurnState();
        if (state == null || !state.UseAction())
            return false;

        Player.ConsumeItem("healing_potion", 1);
        int healRoll = Math.Max(1, DiceRoller.RollExpression("2d4+2"));
        int before = Player.CurrentHP;
        Player.CurrentHP = Math.Min(Player.MaximumHP, Player.CurrentHP + healRoll);
        healedAmount = Player.CurrentHP - before;
        return true;
    }

    public bool UseCurrentBonusAction()
    {
        if (!IsCombatActive)
            return false;

        CombatTurnState state = GetCurrentTurnState();
        return state != null && state.UseBonusAction();
    }

    public int UseSecondWind()
    {
        if (!IsCombatActive || SecondWindUsedInEncounter)
            return 0;

        if (!UseCurrentBonusAction())
            return 0;

        SecondWindUsedInEncounter = true;
        int heal = DiceRoller.Roll(1, 10) + Player.Level;
        int before = Player.CurrentHP;
        Player.CurrentHP = Math.Min(Player.MaximumHP, Player.CurrentHP + heal);
        return Player.CurrentHP - before;
    }

    public bool UseCurrentReaction()
    {
        if (!IsCombatActive)
            return false;

        CombatTurnState state = GetCurrentTurnState();
        return state != null && state.UseReaction();
    }

    public void NextTurn()
    {
        if (!IsCombatActive || TurnOrder.Count == 0)
            return;

        if (Player.CurrentHP <= 0 || AreAllMonstersDead())
        {
            IsCombatActive = false;
            return;
        }

        int attempts = 0;
        do
        {
            CurrentTurnIndex++;
            if (CurrentTurnIndex >= TurnOrder.Count)
            {
                CurrentTurnIndex = 0;
                RoundNumber++;
            }

            attempts++;
            if (IsCombatantAlive(TurnOrder[CurrentTurnIndex]))
                break;
        }
        while (attempts <= TurnOrder.Count);

        ResetCurrentTurn();
    }

    public void EndTurn()
    {
        if (!IsCombatActive)
            return;

        NextTurn();
    }

    public CombatAction PerformWeaponAttack(
        string weaponId,
        CombatMonster target,
        RollAdvantage externalAdvantage = RollAdvantage.Normal)
    {
        if (!IsCombatActive)
            return null;

        InitiativeResult current = GetCurrentCombatant();
        if (current == null || !current.IsPlayer)
            return null;

        if (CombatSystem.CannotAct(Player.ActiveConditions))
            return null;

        CombatTurnState turnState = GetCurrentTurnState();
        if (turnState == null || !turnState.ActionAvailable)
            return null;

        if (target == null || target.IsDead())
            return null;

        WeaponData weapon = DataManager.Instance.GetWeaponData(weaponId);
        if (weapon == null)
            return null;

        bool isRanged = weapon.Category != null && weapon.Category.Contains("ranged", StringComparison.OrdinalIgnoreCase);
        if (!isRanged && EngagementDistance > 5)
            return null;

        bool hasAdv = externalAdvantage == RollAdvantage.Advantage ||
                      PlayerNextAttackAdvantage ||
                      CombatSystem.HasIncomingAttackAdvantage(target.ActiveConditions);

        bool hasDisadv = externalAdvantage == RollAdvantage.Disadvantage ||
                         Player.NextAttackDisadvantage ||
                         CombatSystem.HasAttackDisadvantage(Player.ActiveConditions);

        RollAdvantage effectiveAdv = CombatSystem.CombineAdvantage(hasAdv, hasDisadv);
        PlayerNextAttackAdvantage = false;
        Player.NextAttackDisadvantage = false;

        CombatAttackResult attackResult = CombatSystem.PerformWeaponAttack(
            Player,
            weapon,
            target,
            effectiveAdv
        );

        if (attackResult.Hit && string.Equals(weapon.Mastery, "Vex", StringComparison.OrdinalIgnoreCase))
            PlayerNextAttackAdvantage = true;

        if (!turnState.UseAction())
            return null;

        var action = new CombatAction
        {
            ActionType = "weapon_attack",
            ActorId = Player.Id,
            TargetId = target.InstanceId,
            WeaponId = weapon.Id,
            AttackResult = attackResult
        };

        if (AreAllMonstersDead())
            IsCombatActive = false;

        return action;
    }

    public SpellCastResult PerformSpellCast(string spellId, CombatMonster target)
    {
        if (!IsCombatActive)
            return null;

        InitiativeResult current = GetCurrentCombatant();
        if (current == null || !current.IsPlayer)
            return null;

        if (CombatSystem.CannotAct(Player.ActiveConditions))
            return null;

        CombatTurnState turnState = GetCurrentTurnState();
        if (turnState == null || !turnState.ActionAvailable)
            return null;

        SpellData spell = DataManager.Instance?.GetSpellData(spellId);
        if (spell == null)
            return null;

        if (string.IsNullOrEmpty(spell.HealingDice) && (target == null || target.IsDead()))
            return null;

        RollAdvantage adv = RollAdvantage.Normal;
        if (spell.AttackRequired)
        {
            bool hasAdv = PlayerNextAttackAdvantage || (target != null && CombatSystem.HasIncomingAttackAdvantage(target.ActiveConditions));
            bool hasDisadv = Player.NextAttackDisadvantage || CombatSystem.HasAttackDisadvantage(Player.ActiveConditions);
            adv = CombatSystem.CombineAdvantage(hasAdv, hasDisadv);
            PlayerNextAttackAdvantage = false;
            Player.NextAttackDisadvantage = false;
        }

        SpellCastResult result = CombatSystem.CastSpell(Player, spell, target, adv);
        if (result == null)
            return null;

        turnState.UseAction();

        if (AreAllMonstersDead())
            IsCombatActive = false;

        return result;
    }

    public CombatAction PerformMonsterAttack(
        CombatMonster monster,
        CharacterData target,
        RollAdvantage externalAdvantage = RollAdvantage.Normal)
    {
        if (!IsCombatActive || monster == null || target == null)
            return null;

        InitiativeResult current = GetCurrentCombatant();
        if (current == null || current.CombatantId != monster.InstanceId)
            return null;

        if (monster.IsDead() || target.CurrentHP <= 0)
            return null;

        if (CombatSystem.CannotAct(monster.ActiveConditions))
        {
            NextTurn();
            return null;
        }

        if (monster.Data.Attacks == null || monster.Data.Attacks.Count == 0)
            return null;

        if (EngagementDistance > 5)
        {
            int monsterSpeed = monster.Data.Combat?.Speed?.Walking ?? 30;
            int step = Math.Min(monsterSpeed, EngagementDistance - 5);
            EngagementDistance = Math.Max(5, EngagementDistance - step);
        }

        if (EngagementDistance > 5)
        {
            NextTurn();
            return null;
        }

        bool hasAdv = externalAdvantage == RollAdvantage.Advantage ||
                      CombatSystem.HasIncomingAttackAdvantage(target.ActiveConditions);

        bool hasDisadv = externalAdvantage == RollAdvantage.Disadvantage ||
                         target.IsDodging ||
                         monster.NextAttackDisadvantage ||
                         CombatSystem.HasAttackDisadvantage(monster.ActiveConditions);

        RollAdvantage effectiveAdv = CombatSystem.CombineAdvantage(hasAdv, hasDisadv);
        monster.NextAttackDisadvantage = false;

        MonsterAttackData attackData = monster.Data.Attacks[0];
        CombatAttackResult attackResult = CombatSystem.PerformMonsterAttack(
            monster,
            attackData,
            target,
            effectiveAdv
        );

        if (attackResult == null)
            return null;

        var action = new CombatAction
        {
            ActionType = "weapon_attack",
            ActorId = monster.InstanceId,
            TargetId = target.Id,
            WeaponId = attackData.WeaponId,
            AttackResult = attackResult
        };

        if (target.CurrentHP <= 0)
            IsCombatActive = false;
        else
            NextTurn();

        return action;
    }
}
