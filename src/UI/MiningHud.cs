using Godot;
using TenMillionBlocks.Automation;
using TenMillionBlocks.Mining;
using TenMillionBlocks.Skills;
using TenMillionBlocks.World;
using TenMillionBlocks.World.Rendering;

namespace TenMillionBlocks.UI;

public partial class MiningHud : CanvasLayer
{
    private VirtualWorld _world = null!;
    private MiningService _mining = null!;
    private WorldView _view = null!;
    private SkillTreeService _skills = null!;
    private MinerSimulationService _miners = null!;

    private Label _blocks = null!;
    private Label _currency = null!;
    private Label _manual = null!;
    private Label _automation = null!;
    private Label _debug = null!;
    private Label _feedback = null!;
    private double _feedbackTime;

    public void Initialize(
        VirtualWorld world,
        MiningService mining,
        WorldView view,
        SkillTreeService skills,
        MinerSimulationService miners)
    {
        _world = world;
        _mining = mining;
        _view = view;
        _skills = skills;
        _miners = miners;
        mining.BlockMined += OnBlockMined;
        mining.CurrencyChanged += _ => Refresh();
        skills.Changed += Refresh;
        miners.Changed += Refresh;
    }

    public override void _Ready()
    {
        var root = new Control
        {
            Name = "MiningHudRoot",
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(root);

        var panel = new PanelContainer
        {
            OffsetLeft = 16.0f,
            OffsetTop = 205.0f,
            OffsetRight = 382.0f,
            OffsetBottom = 414.0f,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        root.AddChild(panel);

        var margin = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        margin.AddThemeConstantOverride("margin_left", 10);
        margin.AddThemeConstantOverride("margin_top", 8);
        margin.AddThemeConstantOverride("margin_right", 10);
        margin.AddThemeConstantOverride("margin_bottom", 8);
        panel.AddChild(margin);

        var column = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        column.AddThemeConstantOverride("separation", 4);
        margin.AddChild(column);

        column.AddChild(new Label { Text = _world.Profile.DisplayName, MouseFilter = Control.MouseFilterEnum.Ignore });
        _blocks = new Label { MouseFilter = Control.MouseFilterEnum.Ignore };
        _currency = new Label { MouseFilter = Control.MouseFilterEnum.Ignore };
        _manual = new Label { MouseFilter = Control.MouseFilterEnum.Ignore };
        _automation = new Label { MouseFilter = Control.MouseFilterEnum.Ignore };
        _feedback = new Label { MouseFilter = Control.MouseFilterEnum.Ignore };
        _debug = new Label { MouseFilter = Control.MouseFilterEnum.Ignore };
        column.AddChild(_blocks);
        column.AddChild(_currency);
        column.AddChild(_manual);
        column.AddChild(_automation);
        column.AddChild(new Label
        {
            Text = "[K] Skill Tree   [M] Place Line Miner on hovered block",
            MouseFilter = Control.MouseFilterEnum.Ignore,
        });
        column.AddChild(_feedback);
        column.AddChild(_debug);

        Refresh();
    }

    public override void _Process(double delta)
    {
        if (_feedbackTime > 0.0)
        {
            _feedbackTime -= delta;
            if (_feedbackTime <= 0.0 && _feedback is not null)
            {
                _feedback.Text = string.Empty;
            }
        }

        if (_debug is not null)
        {
            _debug.Text = $"render chunks: {_view.VisibleChunkCount}  dirty: {_view.PendingChunkRebuilds}  modified: {_world.State.ModifiedChunkCount}";
        }
    }

    private void OnBlockMined(MiningResult result)
    {
        Refresh();
        if (_feedback is not null)
        {
            string source = result.Source == MiningSource.Automated ? "Auto" : "Mined";
            _feedback.Text = $"{source}: {result.BlockId}  +{result.Reward}";
            _feedbackTime = 0.7;
        }
    }

    private void Refresh()
    {
        if (_blocks is not null)
        {
            _blocks.Text = $"Blocks: {_mining.TotalMined:N0} mined  |  {_mining.Remaining:N0} remaining";
        }

        if (_currency is not null)
        {
            _currency.Text = $"Resources: {_mining.Currency:N0}";
        }

        if (_manual is not null)
        {
            _manual.Text = $"Manual mining: {_skills.Derived.ManualBlocksPerClick} block(s) / click";
        }

        if (_automation is not null)
        {
            string unlock = _skills.IsMinerUnlocked("line_miner") ? "unlocked" : "locked in Skill Tree";
            _automation.Text = $"Automation: {_miners.Miners.Count} miner(s), {_miners.BlocksPerSecond:0.##} blocks/s  |  Line Miner {unlock}";
        }
    }
}
