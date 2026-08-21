using Godot;

namespace TenMillionBlocks.UI;

public partial class PauseMenuView
{
    /// <summary>
    /// Restores the pause menu after a failed save/navigation attempt. The return button disables
    /// itself before raising its request so repeated clicks cannot race; if the request fails we make
    /// it usable again and keep the game paused rather than implying that progress was saved.
    /// </summary>
    public void ReportReturnFailure(string message)
    {
        if (_root is null) return;

        _root.Visible = true;
        _mainPanel.Visible = true;
        _settingsPanel.Visible = false;
        GetTree().Paused = true;

        if (_status is not null)
        {
            _status.Text = message;
            _status.Modulate = new Color(1.0f, 0.58f, 0.52f);
        }

        ReenableReturnButton(_mainPanel);
    }

    private static void ReenableReturnButton(Node node)
    {
        foreach (Node child in node.GetChildren())
        {
            if (child is Button button
                && button.Text == "SAVE & RETURN TO MAIN MENU")
            {
                button.Disabled = false;
                return;
            }

            ReenableReturnButton(child);
        }
    }
}
