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

    private PanelContainer _panel = null!;
    private Label _summary = null!;
    private Label _automation = null!;
    private Label _feedback = null!;
    private Label _details = null!;
    private bool _detailsVisible;
    private double _feedbackTime;
    private double _detailRefreshTimer;

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
        Layer = 20;
        var root = new Control
        {
            Name = "MiningHudRoot",
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(root);

        // Keep the playfield clear. The old fixed middle-left information block covered a large part
        // of the cube; this compact dock lives against the lower edge and expands only on request.
        _panel = new PanelContainer
        {
            AnchorTop = 1.0f,
            AnchorBottom = 1.0f,
            OffsetLeft = 16.0f,
            OffsetTop = -82.0f,
            OffsetRight = 610.0f,
            OffsetBottom = -16.0f,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        root.AddChild(_panel);

        var margin = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        margin.AddThemeConstantOverride("margin_left", 10);
        margin.AddThemeConstantOverride("margin_top", 7);
        margin.AddThemeConstantOverride("margin_right", 10);
        margin.AddThemeConstantOverride("margin_bottom", 7);
        _panel.AddChild(margin);

        var column = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        column.AddThemeConstantOverride("separation", 2);
        margin.AddChild(column);

        _summary = new Label { MouseFilter = Control.MouseFilterEnum.Ignore };
        _automation = new Label { MouseFilter = Control.MouseFilterEnum.Ignore };
        _feedback = new Label { MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        _details = new Label
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Visible = false,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };

        column.AddChild(_summary);
        column.AddChild(_automation);
        column.AddChild(_feedback);
        column.AddChild(_details);

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
                _feedback.Visible = false;
            }
        }

        if (_detailsVisible)
        {
            _detailRefreshTimer += delta;
            if (_detailRefreshTimer >= 0.25)
            {
                _detailRefreshTimer = 0.0;
                RefreshDetails();
            }
        }
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is not InputEventKey key || !key.Pressed || key.Echo || key.Keycode != Key.H)
        {
            return;
        }

        _detailsVisible = !_detailsVisible;
        _details.Visible = _detailsVisible;
        _panel.OffsetTop = _detailsVisible ? -174.0f : -82.0f;
        if (_detailsVisible) RefreshDetails();
        GetViewport().SetInputAsHandled();
    }

    private void OnBlockMined(MiningResult result)
    {
        Refresh();
        if (_feedback is not null)
        {
            string source = result.Source == MiningSource.Automated ? "Auto" : "Mined";
            _feedback.Text = $"{source}: {result.BlockId}  +{result.Reward}";
            _feedback.Visible = true;
            _feedbackTime = 0.7;
        }
    }

    private void Refresh()
    {
        if (_summary is not null)
        {
            _summary.Text =
                $"{_world.Profile.DisplayName}  |  {_mining.Remaining:N0} left  |  {_mining.Currency:N0} resources  |  {_skills.Derived.ManualBlocksPerClick}/click";
        }

        if (_automation is not null)
        {
            string drill = _skills.IsMinerUnlocked("line_miner") ? "Drill ready" : "Drill locked";
            string shovel = _skills.IsMinerUnlocked("shovel_miner")
                ? $"Shovel ready (search {_skills.Derived.ShovelSearchRadius})"
                : "Shovel locked";
            _automation.Text =
                $"{_miners.Miners.Count} miners  |  {_miners.BlocksPerSecond:0.##} base blocks/s  |  {drill}  |  {shovel}  |  [H] details";
        }

        if (_detailsVisible) RefreshDetails();
    }

    private void RefreshDetails()
    {
        if (_details is null) return;

        _details.Text =
            $"Controls: [K] Skill Tree   [M] Drill   [N] Powered Shovel\n" +
            $"Mined: {_mining.TotalMined:N0}   render chunks: {_view.VisibleChunkCount}   dirty: {_view.PendingChunkRebuilds}   modified: {_world.State.ModifiedChunkCount}\n" +
            $"Shovel search radius: {_skills.Derived.ShovelSearchRadius} (Terrain Scout increases it to 5)";
    }
}
