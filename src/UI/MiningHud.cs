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
    private ProgressBar _progress = null!;
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
        mining.BlockDamaged += OnBlockDamaged;
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

        _panel = new PanelContainer
        {
            AnchorTop = 1.0f,
            AnchorBottom = 1.0f,
            OffsetLeft = 16.0f,
            OffsetTop = -94.0f,
            OffsetRight = 690.0f,
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
        _progress = new ProgressBar
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
            MinValue = 0.0,
            MaxValue = 100.0,
            ShowPercentage = false,
            CustomMinimumSize = new Vector2(0.0f, 7.0f),
        };
        _automation = new Label { MouseFilter = Control.MouseFilterEnum.Ignore };
        _feedback = new Label { MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        _details = new Label
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Visible = false,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };

        column.AddChild(_summary);
        column.AddChild(_progress);
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
        _panel.OffsetTop = _detailsVisible ? -222.0f : -94.0f;
        if (_detailsVisible) RefreshDetails();
        GetViewport().SetInputAsHandled();
    }

    private void OnBlockMined(MiningResult result)
    {
        Refresh();
        if (_feedback is null) return;

        if (result.BlockId.StartsWith("gem_", System.StringComparison.Ordinal))
        {
            _feedback.Text = $"Gem found: {result.BlockId.Replace("gem_", string.Empty)}  +{result.Reward}";
            _feedback.Modulate = new Color(0.72f, 0.92f, 1.0f);
            _feedbackTime = 1.4;
        }
        else
        {
            string source = result.Source == MiningSource.Automated ? "Auto" : "Mined";
            _feedback.Text = $"{source}: {result.BlockId}  +{result.Reward}";
            _feedback.Modulate = Colors.White;
            _feedbackTime = 0.65;
        }
        _feedback.Visible = true;
    }

    private void OnBlockDamaged(MiningResult result)
    {
        if (_feedback is null) return;
        _feedback.Text = $"Unstable block: hit {result.DamageStage}/{result.DamageRequired}";
        _feedback.Modulate = new Color(1.0f, 0.78f, 0.40f);
        _feedback.Visible = true;
        _feedbackTime = 1.0;
    }

    private void Refresh()
    {
        if (_summary is not null)
        {
            long total = _mining.TotalMined + _mining.Remaining;
            double percent = total <= 0 ? 100.0 : _mining.TotalMined * 100.0 / total;
            _summary.Text =
                $"{_world.Profile.DisplayName}  |  {_mining.Remaining:N0} left  |  {_mining.Currency:N0} resources  |  {percent:0.0}%";
        }

        if (_progress is not null)
        {
            long total = _mining.TotalMined + _mining.Remaining;
            _progress.Value = total <= 0 ? 100.0 : _mining.TotalMined * 100.0 / total;
        }

        if (_automation is not null)
        {
            string drill = _skills.IsMinerUnlocked("line_miner") ? "Drill" : "Drill locked";
            string shovel = _skills.IsMinerUnlocked("shovel_miner") ? "Shovel" : "Shovel locked";
            string rock = _skills.IsMinerUnlocked("pickaxe_miner") ? "Rock" : "Rock locked";
            string forest = _skills.IsMinerUnlocked("axe_miner") ? "Forest" : "Forest locked";
            _automation.Text =
                $"{_miners.Miners.Count} miners  |  {_miners.BlocksPerSecond:0.##} blocks/s  |  {drill} · {shovel} · {rock} · {forest}  |  [H] details";
        }

        if (_detailsVisible) RefreshDetails();
    }

    private void RefreshDetails()
    {
        if (_details is null) return;

        string slope = _skills.Derived.ShovelHeightTolerance > 0
            ? $"+/-{_skills.Derived.ShovelHeightTolerance} height"
            : "same height only";
        string radial = _skills.IsMinerUnlocked("disc_miner") ? "ready" : "locked";
        _details.Text =
            "Place tools: [M] Drill   [N] Shovel   [P] Rock Breaker   [A] Forest Cutter   [B] Radial Excavator\n" +
            "Other: [K] Skill Tree   RMB orbit   MMB pan   wheel zoom\n" +
            $"Drill: {_skills.Derived.DrillPatternId}, width {_skills.Derived.MinerPatternWidth}; Radial: {radial}\n" +
            $"Shovel: {_skills.Derived.ShovelRateMultiplier:0.##}x, {slope}, scout radius {_skills.Derived.ShovelSearchRadius}\n" +
            $"Mined: {_mining.TotalMined:N0}   chunks: {_view.VisibleChunkCount}   queued: {_view.PendingChunkLoads}   dirty: {_view.PendingChunkRebuilds}";
    }
}
