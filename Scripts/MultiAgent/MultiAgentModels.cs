using System;

public enum AgentRoleKind
{
    DungeonMaster,
    PlayerAgent,
    NpcAgent
}

public enum CombatAgentIntent
{
    Attack,
    MoveAndAttack,
    HoldAndTaunt,
    CannotAct
}

public class DmTurnDecision
{
    public int RoundNumber { get; set; }
    public string CombatantId { get; set; } = "";
    public string CombatantName { get; set; } = "";
    public bool IsPlayer { get; set; }
    public CombatAgentIntent RecommendedIntent { get; set; } = CombatAgentIntent.Attack;
    public string PlannedActionLabel { get; set; } = "saldırı";
    public string SummaryLine { get; set; } = "";
}

public class NpcAgentPlayResult
{
    public string AgentName { get; set; } = "";
    public string PersonaTag { get; set; } = "";
    public CombatAgentIntent ChosenIntent { get; set; }
    public string RoleplayLine { get; set; } = "";
}

public class DmPlayerCheckpoint
{
    public bool CanAct { get; set; } = true;
    public bool IsUnconscious { get; set; }
    public string BlockingReason { get; set; } = "";
    public string SummaryLine { get; set; } = "";
}

public class DmResolutionResult
{
    public string MechanicsLine { get; set; } = "";
    public string DmNarrative { get; set; } = "";
    public DmPlayerCheckpoint Checkpoint { get; set; } = new();
}
