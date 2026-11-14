using Godot;
using System;
using System.Collections.Generic;

public partial class CombatMapView : Control
{
    private CombatEncounter _encounter;
    private string _selectedTargetId;

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(0, 128);
        ClipContents = true;
    }

    public void UpdateMap(CombatEncounter encounter, string selectedTargetId = null)
    {
        _encounter = encounter;
        _selectedTargetId = selectedTargetId;
        Visible = encounter != null;
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_encounter == null)
            return;

        Vector2 size = Size;
        if (size.X < 40 || size.Y < 40)
            return;

        // Background arena
        DrawRect(new Rect2(Vector2.Zero, size), new Color(0.11f, 0.12f, 0.15f, 1f));
        DrawRect(new Rect2(Vector2.Zero, size), new Color(0.32f, 0.35f, 0.42f, 1f), filled: false, width: 1.5f);

        float padLeft = 36f;
        float padRight = 44f;
        float usableWidth = Math.Max(80f, size.X - padLeft - padRight);

        const float maxFeet = 60f;
        float FeetToX(float ft) => padLeft + (Math.Clamp(ft, 0f, maxFeet) / maxFeet) * usableWidth;

        // 5-ft grid lines & ruler labels
        Font font = ThemeDB.FallbackFont;
        for (int ft = 0; ft <= (int)maxFeet; ft += 5)
        {
            float x = FeetToX(ft);
            bool major = ft % 15 == 0;
            Color lineColor = major ? new Color(0.28f, 0.31f, 0.38f, 0.9f) : new Color(0.20f, 0.22f, 0.27f, 0.6f);
            DrawLine(new Vector2(x, 6f), new Vector2(x, size.Y - 18f), lineColor, major ? 1.2f : 1f);

            if (major && font != null)
                DrawString(font, new Vector2(x - 10f, size.Y - 4f), $"{ft}ft", HorizontalAlignment.Left, -1, 10, new Color(0.65f, 0.70f, 0.78f));
        }

        float playerX = FeetToX(0f);
        float playerY = (size.Y - 18f) * 0.52f;
        float enemyX = FeetToX(_encounter.EngagementDistance);

        // Melee threshold indicator (5 ft zone)
        float meleeX = FeetToX(5f);
        DrawRect(
            new Rect2(playerX - 10f, 8f, (meleeX - playerX) + 20f, size.Y - 26f),
            new Color(0.20f, 0.55f, 0.35f, 0.10f)
        );

        // Distance line between player and enemies
        bool inMelee = _encounter.EngagementDistance <= 5;
        Color distColor = inMelee ? new Color(0.35f, 0.85f, 0.50f, 0.95f) : new Color(0.95f, 0.75f, 0.30f, 0.95f);
        DrawLine(new Vector2(playerX, playerY), new Vector2(enemyX, playerY), distColor, 2.0f);

        // Distance badge centered on line
        string distLabel = inMelee ? $"{_encounter.EngagementDistance} ft (Yakın)" : $"{_encounter.EngagementDistance} ft";
        float midX = (playerX + enemyX) * 0.5f;
        Vector2 badgePos = new Vector2(Math.Clamp(midX - 32f, padLeft, size.X - 88f), 19f);
        DrawRect(new Rect2(badgePos.X - 4f, badgePos.Y - 12f, 78f, 16f), new Color(0.08f, 0.09f, 0.12f, 0.88f));
        if (font != null)
            DrawString(font, badgePos, distLabel, HorizontalAlignment.Left, -1, 11, distColor);

        // Player token
        CharacterData player = _encounter.Player;
        Color playerColor = player.CurrentHP > 0 ? new Color(0.22f, 0.62f, 0.96f) : new Color(0.45f, 0.25f, 0.25f);
        DrawCircle(new Vector2(playerX, playerY), 12f, playerColor);
        DrawArc(new Vector2(playerX, playerY), 12f, 0f, Mathf.Tau, 24, Colors.White, 1.5f);
        if (font != null)
        {
            DrawString(font, new Vector2(playerX - 18f, playerY + 24f), player.Name, HorizontalAlignment.Left, -1, 11, Colors.White);
            DrawHpMiniBar(new Vector2(playerX - 14f, playerY - 20f), 28f, player.CurrentHP, player.MaximumHP, new Color(0.25f, 0.85f, 0.45f));
        }

        // Monster tokens stacked vertically at enemyX
        List<CombatMonster> monsters = _encounter.Monsters;
        int count = monsters.Count;
        float topY = 26f;
        float bottomY = size.Y - 26f;

        for (int i = 0; i < count; i++)
        {
            CombatMonster m = monsters[i];
            float y = count == 1
                ? playerY
                : Mathf.Lerp(topY, bottomY, (float)i / Math.Max(1, count - 1));

            bool isDead = m.IsDead();
            bool isSelected = !isDead && m.InstanceId == _selectedTargetId;

            if (isSelected)
                DrawArc(new Vector2(enemyX, y), 15f, 0f, Mathf.Tau, 24, new Color(1.0f, 0.85f, 0.25f), 2.2f);

            Color monsterColor = isDead ? new Color(0.35f, 0.35f, 0.38f) : new Color(0.88f, 0.28f, 0.28f);
            DrawCircle(new Vector2(enemyX, y), 11f, monsterColor);
            DrawArc(new Vector2(enemyX, y), 11f, 0f, Mathf.Tau, 24, isDead ? new Color(0.55f, 0.55f, 0.58f) : Colors.White, 1.3f);

            if (isDead)
            {
                DrawLine(new Vector2(enemyX - 5f, y - 5f), new Vector2(enemyX + 5f, y + 5f), Colors.LightGray, 2f);
                DrawLine(new Vector2(enemyX + 5f, y - 5f), new Vector2(enemyX - 5f, y + 5f), Colors.LightGray, 2f);
            }
            else
            {
                DrawHpMiniBar(new Vector2(enemyX - 14f, y - 18f), 28f, m.CurrentHP, m.MaximumHP, new Color(0.95f, 0.32f, 0.32f));
            }

            if (font != null)
            {
                string label = isDead ? $"{m.DisplayName} (Ölü)" : $"{m.DisplayName}";
                DrawString(font, new Vector2(enemyX + 14f, y + 4f), label, HorizontalAlignment.Left, -1, 10, isDead ? Colors.Gray : Colors.White);
            }
        }
    }

    private void DrawHpMiniBar(Vector2 pos, float width, int current, int max, Color fill)
    {
        float ratio = max > 0 ? Math.Clamp((float)current / max, 0f, 1f) : 0f;
        DrawRect(new Rect2(pos, new Vector2(width, 4f)), new Color(0.05f, 0.05f, 0.05f, 0.9f));
        if (ratio > 0f)
            DrawRect(new Rect2(pos, new Vector2(width * ratio, 4f)), fill);
    }
}
