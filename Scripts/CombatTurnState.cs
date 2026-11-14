public class CombatTurnState
{
    public int MovementRemaining { get; set; }

    public bool ActionAvailable { get; set; }

    public bool BonusActionAvailable { get; set; }

    public bool ReactionAvailable { get; set; }

    public CombatTurnState(int walkingSpeed)
    {
        MovementRemaining = walkingSpeed;

        ActionAvailable = true;
        BonusActionAvailable = true;
        ReactionAvailable = true;
    }

    public void Reset(int walkingSpeed)
    {
        MovementRemaining = walkingSpeed;

        ActionAvailable = true;
        BonusActionAvailable = true;
        ReactionAvailable = true;
    }

    public bool UseAction()
    {
        if (!ActionAvailable)
            return false;

        ActionAvailable = false;

        return true;
    }

    public bool UseBonusAction()
    {
        if (!BonusActionAvailable)
            return false;

        BonusActionAvailable = false;

        return true;
    }

    public bool UseReaction()
    {
        if (!ReactionAvailable)
            return false;

        ReactionAvailable = false;

        return true;
    }

    public bool UseMovement(int amount)
    {
        if (amount <= 0)
            return false;

        if (amount > MovementRemaining)
            return false;

        MovementRemaining -= amount;

        return true;
    }
}