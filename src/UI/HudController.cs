using Godot;
using TenMillionBlocks.Core;
using TenMillionBlocks.Gameplay;
using TenMillionBlocks.World;

namespace TenMillionBlocks.UI;

public sealed partial class HudController : CanvasLayer
{
    private GameState? _state;
    private UpgradeSystem? _upgrades;
    private VoxelWorld? _world;

    private Label? _stageLabel;
    private Label? _currencyLabel;
    private Label? _blocksLabel;
    private Label? _powerLabel;
    private Label? _autoLabel;
    private Label? _feedbackLabel;
    private Label? _bannerLabel;
    private ProgressBar? _progress;
    private Button? _powerButton;
    private Button? _speedButton;
    private Button? _autoButton;

    public void Initialize(
        GameState state,
        UpgradeSystem upgrades,
        VoxelWorld world,
        MiningService miningService)
    {
        _state = state;
        _upgrades = upgrades;
        _world = world;

        BuildUi();

        state.Changed += Refresh;
        upgrades.Changed += Refresh;
        world.BlockCountChanged += (_, _) => Refresh();
        miningService.Feedback += OnMiningFeedback;

        Refresh();
    }

    public void ShowBanner(string text)
    {
        if (_bannerLabel is null)
        {
            return;
        }

        _bannerLabel.Text = text;
        _bannerLabel.Visible = true;
    }

    public void HideBanner()
    {
        if (_bannerLabel is not null)
        {
            _bannerLabel.Visible = false;
        }
    }

    private void BuildUi()
    {
        var root = new Control
        {
            Name = "HudRoot",
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(root);

        PanelContainer statsPanel = CreatePanel();
        statsPanel.Position = new Vector2(22, 22);
        statsPanel.CustomMinimumSize = new Vector2(340, 190);
        root.AddChild(statsPanel);

        var stats = new VBoxContainer
        {
            CustomMinimumSize = new Vector2(310, 0),
        };
        stats.AddThemeConstantOverride("separation", 6);
        statsPanel.AddChild(stats);

        Label title = CreateLabel("10 MILLION BLOCKS", 24);
        stats.AddChild(title);

        _stageLabel = CreateLabel("", 15);
        _stageLabel.Modulate = new Color(0.63f, 0.83f, 1.0f);
        stats.AddChild(_stageLabel);

        _blocksLabel = CreateLabel("", 18);
        stats.AddChild(_blocksLabel);

        _progress = new ProgressBar
        {
            CustomMinimumSize = new Vector2(0, 18),
            ShowPercentage = false,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        stats.AddChild(_progress);

        _currencyLabel = CreateLabel("", 19);
        _currencyLabel.Modulate = new Color(1.0f, 0.85f, 0.35f);
        stats.AddChild(_currencyLabel);

        var statsRow = new HBoxContainer();
        statsRow.AddThemeConstantOverride("separation", 16);
        _powerLabel = CreateLabel("", 13);
        _autoLabel = CreateLabel("", 13);
        statsRow.AddChild(_powerLabel);
        statsRow.AddChild(_autoLabel);
        stats.AddChild(statsRow);

        PanelContainer upgradePanel = CreatePanel();
        upgradePanel.AnchorLeft = 1.0f;
        upgradePanel.AnchorRight = 1.0f;
        upgradePanel.OffsetLeft = -332.0f;
        upgradePanel.OffsetRight = -22.0f;
        upgradePanel.OffsetTop = 22.0f;
        upgradePanel.CustomMinimumSize = new Vector2(310, 270);
        root.AddChild(upgradePanel);

        var upgradesBox = new VBoxContainer
        {
            CustomMinimumSize = new Vector2(280, 0),
        };
        upgradesBox.AddThemeConstantOverride("separation", 9);
        upgradePanel.AddChild(upgradesBox);

        upgradesBox.AddChild(CreateLabel("UPGRADES", 20));
        Label subtitle = CreateLabel("Spend mined blocks to accelerate the collapse.", 12);
        subtitle.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        subtitle.Modulate = new Color(0.72f, 0.75f, 0.82f);
        upgradesBox.AddChild(subtitle);

        _powerButton = CreateUpgradeButton();
        _powerButton.Pressed += () => Purchase(UpgradeKind.PickaxePower);
        upgradesBox.AddChild(_powerButton);

        _speedButton = CreateUpgradeButton();
        _speedButton.Pressed += () => Purchase(UpgradeKind.MiningSpeed);
        upgradesBox.AddChild(_speedButton);

        _autoButton = CreateUpgradeButton();
        _autoButton.Pressed += () => Purchase(UpgradeKind.AutoMiners);
        upgradesBox.AddChild(_autoButton);

        _bannerLabel = CreateLabel("", 32);
        _bannerLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _bannerLabel.VerticalAlignment = VerticalAlignment.Center;
        _bannerLabel.AnchorLeft = 0.5f;
        _bannerLabel.AnchorTop = 0.5f;
        _bannerLabel.AnchorRight = 0.5f;
        _bannerLabel.AnchorBottom = 0.5f;
        _bannerLabel.OffsetLeft = -310;
        _bannerLabel.OffsetTop = -55;
        _bannerLabel.OffsetRight = 310;
        _bannerLabel.OffsetBottom = 55;
        _bannerLabel.Visible = false;
        _bannerLabel.MouseFilter = Control.MouseFilterEnum.Ignore;
        root.AddChild(_bannerLabel);

        _feedbackLabel = CreateLabel("", 15);
        _feedbackLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _feedbackLabel.AnchorLeft = 0.5f;
        _feedbackLabel.AnchorRight = 0.5f;
        _feedbackLabel.AnchorTop = 1.0f;
        _feedbackLabel.AnchorBottom = 1.0f;
        _feedbackLabel.OffsetLeft = -250;
        _feedbackLabel.OffsetRight = 250;
        _feedbackLabel.OffsetTop = -78;
        _feedbackLabel.OffsetBottom = -48;
        _feedbackLabel.MouseFilter = Control.MouseFilterEnum.Ignore;
        root.AddChild(_feedbackLabel);

        Label hint = CreateLabel(
            "LMB / hold  MINE     •     RMB drag  ORBIT     •     WHEEL  ZOOM",
            13);
        hint.HorizontalAlignment = HorizontalAlignment.Center;
        hint.AnchorLeft = 0.5f;
        hint.AnchorRight = 0.5f;
        hint.AnchorTop = 1.0f;
        hint.AnchorBottom = 1.0f;
        hint.OffsetLeft = -390;
        hint.OffsetRight = 390;
        hint.OffsetTop = -42;
        hint.OffsetBottom = -14;
        hint.Modulate = new Color(0.70f, 0.74f, 0.82f);
        hint.MouseFilter = Control.MouseFilterEnum.Ignore;
        root.AddChild(hint);
    }

    private void Refresh()
    {
        if (_state is null || _upgrades is null || _world is null)
        {
            return;
        }

        int target = _world.TargetBlockCount <= 0 ? _state.CurrentStageTarget : _world.TargetBlockCount;
        int remaining = _world.RemainingBlockCount;
        int mined = target - remaining;

        _stageLabel!.Text = $"WORLD {_state.StageIndex + 1}  •  {target:N0} BLOCKS";
        _blocksLabel!.Text = $"{remaining:N0} blocks remaining";
        _currencyLabel!.Text = $"▣  {_state.Currency:N0} mined blocks";

        _progress!.MaxValue = target;
        _progress.Value = mined;

        _powerLabel!.Text = $"POWER  ×{_upgrades.ManualDamage:0.#}";
        _autoLabel!.Text = _upgrades.AutoMinerLevel <= 0
            ? "AUTO  OFF"
            : $"AUTO  {_upgrades.AutoBatchSize} / tick";

        SetUpgradeButton(
            _powerButton!,
            $"PICKAXE POWER  Lv.{_upgrades.PickaxePowerLevel}",
            $"Double damage  •  {_upgrades.GetCost(UpgradeKind.PickaxePower):N0} ▣",
            UpgradeKind.PickaxePower);

        SetUpgradeButton(
            _speedButton!,
            $"MINING SPEED  Lv.{_upgrades.MiningSpeedLevel}",
            $"Faster manual mining  •  {_upgrades.GetCost(UpgradeKind.MiningSpeed):N0} ▣",
            UpgradeKind.MiningSpeed);

        SetUpgradeButton(
            _autoButton!,
            $"AUTO MINERS  Lv.{_upgrades.AutoMinerLevel}",
            $"Mine surface blocks automatically  •  {_upgrades.GetCost(UpgradeKind.AutoMiners):N0} ▣",
            UpgradeKind.AutoMiners);
    }

    private void SetUpgradeButton(Button button, string title, string subtitle, UpgradeKind kind)
    {
        if (_state is null || _upgrades is null)
        {
            return;
        }

        button.Text = $"{title}\n{subtitle}";
        button.Disabled = _state.Currency < _upgrades.GetCost(kind);
    }

    private void Purchase(UpgradeKind kind)
    {
        if (_state is null || _upgrades is null)
        {
            return;
        }

        if (_upgrades.TryPurchase(kind, _state))
        {
            Refresh();
        }
    }

    private void OnMiningFeedback(MiningFeedback feedback)
    {
        if (_feedbackLabel is null)
        {
            return;
        }

        BlockDefinition definition = BlockPalette.Get(feedback.Type);
        if (feedback.Destroyed)
        {
            _feedbackLabel.Text = feedback.Automated
                ? $"Auto miner cleared {feedback.Type}  +{feedback.Reward} ▣"
                : $"{feedback.Type} cleared  +{feedback.Reward} ▣";
            _feedbackLabel.Modulate = definition.Color.Lightened(0.25f);
        }
        else if (!feedback.Automated)
        {
            _feedbackLabel.Text = $"{feedback.Type}  •  {Mathf.RoundToInt(feedback.HealthRatio * 100.0f)}% integrity";
            _feedbackLabel.Modulate = definition.Color.Lightened(0.18f);
        }
    }

    private static PanelContainer CreatePanel()
    {
        var panel = new PanelContainer
        {
            MouseFilter = Control.MouseFilterEnum.Stop,
        };

        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.025f, 0.035f, 0.075f, 0.94f),
            BorderColor = new Color(0.15f, 0.26f, 0.42f, 0.92f),
            BorderWidthLeft = 1,
            BorderWidthTop = 1,
            BorderWidthRight = 1,
            BorderWidthBottom = 1,
            CornerRadiusTopLeft = 8,
            CornerRadiusTopRight = 8,
            CornerRadiusBottomLeft = 8,
            CornerRadiusBottomRight = 8,
            ContentMarginLeft = 16,
            ContentMarginRight = 16,
            ContentMarginTop = 14,
            ContentMarginBottom = 14,
        };
        panel.AddThemeStyleboxOverride("panel", style);
        return panel;
    }

    private static Label CreateLabel(string text, int fontSize)
    {
        var label = new Label
        {
            Text = text,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        label.AddThemeFontSizeOverride("font_size", fontSize);
        return label;
    }

    private static Button CreateUpgradeButton()
    {
        var button = new Button
        {
            CustomMinimumSize = new Vector2(0, 58),
            Alignment = HorizontalAlignment.Left,
            MouseDefaultCursorShape = Control.CursorShape.PointingHand,
        };
        button.AddThemeFontSizeOverride("font_size", 13);

        var normal = new StyleBoxFlat
        {
            BgColor = new Color(0.055f, 0.075f, 0.13f, 0.96f),
            BorderColor = new Color(0.16f, 0.28f, 0.43f),
            BorderWidthLeft = 1,
            BorderWidthTop = 1,
            BorderWidthRight = 1,
            BorderWidthBottom = 1,
            CornerRadiusTopLeft = 5,
            CornerRadiusTopRight = 5,
            CornerRadiusBottomLeft = 5,
            CornerRadiusBottomRight = 5,
            ContentMarginLeft = 12,
            ContentMarginRight = 12,
        };

        var hover = (StyleBoxFlat)normal.Duplicate();
        hover.BgColor = new Color(0.075f, 0.12f, 0.20f, 1.0f);
        hover.BorderColor = new Color(0.30f, 0.58f, 0.82f);

        button.AddThemeStyleboxOverride("normal", normal);
        button.AddThemeStyleboxOverride("hover", hover);
        button.AddThemeStyleboxOverride("pressed", hover);
        return button;
    }
}
