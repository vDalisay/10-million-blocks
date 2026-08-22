using Godot;
using TenMillionBlocks.Automation;
using TenMillionBlocks.World.Rendering;

namespace TenMillionBlocks.UI;

public partial class AutomationAttentionView : CanvasLayer
{
    private const double RefreshIntervalSeconds = 0.12;

    private MinerSimulationService _miners = null!;
    private WorldView _view = null!;
    private PanelContainer _panel = null!;
    private Button _button = null!;
    private int _cycleIndex;
    private double _pulseTime;
    private double _refreshCooldown;
    private bool _refreshPending;

    public void Initialize(MinerSimulationService miners, WorldView view)
    {
        _miners = miners;
        _view = view;
        miners.MinerStopped += OnMinerStopped;
        miners.Changed += RequestRefresh;
    }

    public override void _Ready()
    {
        Layer = 28;
        _panel = new PanelContainer
        {
            AnchorLeft = 1.0f,
            AnchorRight = 1.0f,
            OffsetLeft = -420.0f,
            OffsetTop = 106.0f,
            OffsetRight = -16.0f,
            OffsetBottom = 188.0f,
            Visible = false,
        };
        AddChild(_panel);

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 8);
        margin.AddThemeConstantOverride("margin_top", 7);
        margin.AddThemeConstantOverride("margin_right", 8);
        margin.AddThemeConstantOverride("margin_bottom", 7);
        _panel.AddChild(margin);

        _button = new Button
        {
            Text = "Automation needs attention",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        _button.Pressed += CycleAttention;
        margin.AddChild(_button);
        Refresh();
    }

    public override void _Process(double delta)
    {
        _refreshCooldown -= delta;
        if (_refreshPending && _refreshCooldown <= 0.0)
        {
            _refreshPending = false;
            _refreshCooldown = RefreshIntervalSeconds;
            Refresh();
        }

        if (_pulseTime <= 0.0 || _panel is null) return;
        _pulseTime -= delta;
        float t = Mathf.Clamp((float)(_pulseTime / 0.55), 0.0f, 1.0f);
        _panel.Modulate = new Color(1.0f, 1.0f, 1.0f, Mathf.Lerp(0.84f, 1.0f, t));
        if (_pulseTime <= 0.0) _panel.Modulate = Colors.White;
    }

    public override void _ExitTree()
    {
        if (_miners is not null)
        {
            _miners.MinerStopped -= OnMinerStopped;
            _miners.Changed -= RequestRefresh;
            _miners.SetAttentionHighlight(null);
        }
    }

    private void RequestRefresh()
    {
        _refreshPending = true;
    }

    private void OnMinerStopped(MinerInstance miner)
    {
        _cycleIndex = 0;
        _pulseTime = 0.55;
        _refreshPending = false;
        _refreshCooldown = RefreshIntervalSeconds;
        Refresh(miner);
    }

    private void CycleAttention()
    {
        int count = _miners.AttentionMinerCount;
        if (count <= 0)
        {
            Refresh();
            return;
        }

        MinerInstance? miner = _miners.GetAttentionMiner(_cycleIndex++);
        if (miner is null) return;

        _miners.SetAttentionHighlight(miner);
        _view.FocusAutomationVoxel(_miners.AttentionFocusVoxel(miner));
        Refresh(miner);
    }

    private void Refresh()
    {
        Refresh(_miners.GetAttentionMiner(_cycleIndex));
    }

    private void Refresh(MinerInstance? selected)
    {
        if (_panel is null || _button is null) return;

        int count = _miners.AttentionMinerCount;
        _panel.Visible = count > 0;
        if (count <= 0)
        {
            _button.Text = string.Empty;
            _cycleIndex = 0;
            _miners.SetAttentionHighlight(null);
            return;
        }

        selected ??= _miners.GetAttentionMiner(0);
        string detail = selected is null
            ? "automation stopped"
            : $"{selected.DefinitionId}: {_miners.DescribeStop(selected)}";
        _button.Text = count == 1
            ? $"AUTOMATION STOPPED\n{detail}  ·  Click to focus/select"
            : $"{count} AUTOMATIONS NEED ATTENTION\n{detail}  ·  Click to cycle/focus";
    }
}
