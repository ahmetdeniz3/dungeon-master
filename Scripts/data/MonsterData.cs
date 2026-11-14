using System.Collections.Generic;

public class MonsterData
{
    public string Id { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public string Type { get; set; }
    public string Size { get; set; }
    public string Alignment { get; set; }
    public int ChallengeXP { get; set; } = 50;

    public AbilityScores AbilityScores { get; set; }

    public MonsterCombat Combat { get; set; }

    public List<MonsterAttackData> Attacks { get; set; } = new();
}
