using System;
using Godot;
using TenMillionBlocks.World.Rendering;

namespace TenMillionBlocks.UI;

/// <summary>
/// Root-level loading presentation that survives gameplay scene/session replacement. Transition
/// callers show it one rendered frame before starting a synchronous world/replay load, so an expensive
/// deterministic world build never leaves the player staring at a frozen gameplay frame.
///
/// The overlay dismisses itself only after it observes a different WorldView instance in the active
/// scene. This makes the same screen work for main-menu -> game, next-world, revisit and replay loads
/// without coupling the loading UI to GameRoot internals.
/// </summary>
public partial class WorldLoadingScreen : CanvasLayer
{
    private static readonly Color[] BlockPalettes =
    [
        new Color(0.72f, 0.43f, 0.28f), // dirt
        new Color(0.08f, 0.78f, 0.48f), // grass
        new Color(0.77f, 0.74f, 0.67f), // stone
        new Color(0.94f, 0.78f, 0.48f), // sand
        new Color(0.12f, 0.68f, 0.94f), // azure gem
        new Color(0.18f, 0.78f, 0.34f), // verdant gem
        new Color(0.90f, 0.20f, 0.22f), // core gem
    ];

    private static WorldLoadingScreen? _instance;

    private Control _root = null!;
    private Control _block = null!;
    private ColorRect _blockFace = null!;
    private ColorRect _blockTop = null!;
    private ColorRect _blockSide = null!;
    private Label _label = null!;
    private ulong _baselineWorldViewId;
    private int _replacementStableFrames;
    private double _elapsed;
    private double _phase;

    public static void RunTransition(Node context, string label, Action transition)
    {
        if (context is null || transition is null) return;
        _ = RunTransitionAsync(context, label, transition);
    }

    public static void CancelGlobal()
    {
        if (_instance is null || !GodotObject.IsInstanceValid(_instance)) return;
        _instance.HideLoading();
    }

    private static async System.Threading.Tasks.Task RunTransitionAsync(Node context, string label, Action transition)
    {
        SceneTree tree = context.GetTree();
        WorldLoadingScreen loading = Ensure(tree);
        loading.Begin(label);

        // Guarantee that the loading frame is actually presented before the expensive transition
        // starts. Two frames also make the first pulse visible on very fast world changes.
        await context.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        if (!GodotObject.IsInstanceValid(context)) return;
        await context.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        if (!GodotObject.IsInstanceValid(context)) return;

        try
        {
            transition();
        }
        catch (Exception exception)
        {
            loading.HideLoading();
            GD.PushError($"World transition failed: {exception}");
            throw;
        }
    }

    private static WorldLoadingScreen Ensure(SceneTree tree)
    {
        if (_instance is not null && GodotObject.IsInstanceValid(_instance)) return _instance;

        _instance = new WorldLoadingScreen
        {
            Name = "PersistentWorldLoadingScreen",
            Layer = 10000,
            ProcessMode = ProcessModeEnum.Always,
        };
        tree.Root.AddChild(_instance);
        return _instance;
    }

    public override void _Ready()
    {
        BuildUi();
        Visible = false;
    }

    public override void _Process(double delta)
    {
        if (!Visible || _root is null) return;

        _elapsed += Math.Max(0.0, delta);
        _phase += Math.Max(0.0, delta) * 3.25;

        float pulse = 0.92f + 0.09f * (0.5f + 0.5f * MathF.Sin((float)_phase));
        _block.Scale = Vector2.One * pulse;
        _block.Rotation = MathF.Sin((float)_phase * 0.47f) * 0.025f;

        ulong currentWorldViewId = FindWorldViewInstanceId(GetTree().CurrentScene);
        if (currentWorldViewId != 0 && currentWorldViewId != _baselineWorldViewId)
        {
            _replacementStableFrames++;
            if (_replacementStableFrames >= 3)
            {
                HideLoading();
                return;
            }
        }
        else
        {
            _replacementStableFrames = 0;
        }

        // Do not permanently mask a fatal initialization error. Normal reviewed worlds should replace
        // their WorldView long before this fallback is reached.
        if (_elapsed >= 60.0)
        {
            HideLoading();
        }
    }

    private void Begin(string label)
    {
        _baselineWorldViewId = FindWorldViewInstanceId(GetTree().CurrentScene);
        _replacementStableFrames = 0;
        _elapsed = 0.0;
        _phase = 0.0;
        _label.Text = string.IsNullOrWhiteSpace(label) ? "LOADING WORLD" : label.ToUpperInvariant();
        RandomizeBlockPalette();
        _block.Scale = Vector2.One;
        _block.Rotation = 0.0f;
        Visible = true;
    }

    private void HideLoading()
    {
        Visible = false;
        _replacementStableFrames = 0;
        _elapsed = 0.0;
    }

    private void BuildUi()
    {
        _root = new Control
        {
            Name = "LoadingRoot",
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        _root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(_root);

        var backdrop = new ColorRect
        {
            Color = new Color(0.003f, 0.008f, 0.025f, 1.0f),
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        backdrop.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _root.AddChild(backdrop);

        // Lightweight deterministic star field. It is deliberately Canvas-based so it remains visible
        // even while the gameplay scene and its 3D presentation are being replaced.
        var random = new Random(6824);
        for (int i = 0; i < 90; i++)
        {
            float x = 0.02f + (float)random.NextDouble() * 0.96f;
            float y = 0.03f + (float)random.NextDouble() * 0.94f;
            float size = random.NextDouble() > 0.86 ? 2.0f : 1.0f;
            var star = new ColorRect
            {
                Color = new Color(0.72f, 0.82f, 0.96f, 0.55f + (float)random.NextDouble() * 0.35f),
                MouseFilter = Control.MouseFilterEnum.Ignore,
                AnchorLeft = x,
                AnchorRight = x,
                AnchorTop = y,
                AnchorBottom = y,
                OffsetRight = size,
                OffsetBottom = size,
            };
            _root.AddChild(star);
        }

        _block = new Control
        {
            Name = "PulsingBlock",
            MouseFilter = Control.MouseFilterEnum.Ignore,
            AnchorLeft = 0.5f,
            AnchorTop = 0.5f,
            AnchorRight = 0.5f,
            AnchorBottom = 0.5f,
            OffsetLeft = -70.0f,
            OffsetTop = -92.0f,
            OffsetRight = 70.0f,
            OffsetBottom = 48.0f,
            PivotOffset = new Vector2(70.0f, 70.0f),
        };
        _root.AddChild(_block);

        _blockFace = new ColorRect
        {
            Position = new Vector2(18.0f, 24.0f),
            Size = new Vector2(96.0f, 96.0f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _block.AddChild(_blockFace);

        _blockTop = new ColorRect
        {
            Position = new Vector2(18.0f, 16.0f),
            Size = new Vector2(96.0f, 15.0f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _block.AddChild(_blockTop);

        _blockSide = new ColorRect
        {
            Position = new Vector2(107.0f, 24.0f),
            Size = new Vector2(15.0f, 96.0f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _block.AddChild(_blockSide);

        // A few tiny block flecks keep the loader visually related to the supplied block art without
        // loading or instantiating gameplay resources during the transition itself.
        for (int i = 0; i < 15; i++)
        {
            float x = 24.0f + (float)random.NextDouble() * 78.0f;
            float y = 32.0f + (float)random.NextDouble() * 78.0f;
            float size = 1.5f + (float)random.NextDouble() * 2.5f;
            _block.AddChild(new ColorRect
            {
                Position = new Vector2(x, y),
                Size = Vector2.One * size,
                Color = new Color(0.06f, 0.04f, 0.03f, 0.18f),
                MouseFilter = Control.MouseFilterEnum.Ignore,
            });
        }

        _label = new Label
        {
            Text = "LOADING WORLD",
            HorizontalAlignment = HorizontalAlignment.Center,
            AnchorLeft = 0.5f,
            AnchorTop = 0.5f,
            AnchorRight = 0.5f,
            AnchorBottom = 0.5f,
            OffsetLeft = -260.0f,
            OffsetTop = 76.0f,
            OffsetRight = 260.0f,
            OffsetBottom = 116.0f,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _label.AddThemeFontSizeOverride("font_size", 22);
        _root.AddChild(_label);

        var subtitle = new Label
        {
            Text = "PREPARING CUBE...",
            HorizontalAlignment = HorizontalAlignment.Center,
            Modulate = new Color(0.72f, 0.78f, 0.88f),
            AnchorLeft = 0.5f,
            AnchorTop = 0.5f,
            AnchorRight = 0.5f,
            AnchorBottom = 0.5f,
            OffsetLeft = -240.0f,
            OffsetTop = 112.0f,
            OffsetRight = 240.0f,
            OffsetBottom = 144.0f,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _root.AddChild(subtitle);

        RandomizeBlockPalette();
    }

    private void RandomizeBlockPalette()
    {
        Color baseColor = BlockPalettes[Random.Shared.Next(BlockPalettes.Length)];
        _blockFace.Color = baseColor;
        _blockTop.Color = baseColor.Lightened(0.16f);
        _blockSide.Color = baseColor.Darkened(0.18f);
    }

    private static ulong FindWorldViewInstanceId(Node? node)
    {
        if (node is null) return 0;
        if (node is WorldView worldView) return worldView.GetInstanceId();

        foreach (Node child in node.GetChildren())
        {
            ulong found = FindWorldViewInstanceId(child);
            if (found != 0) return found;
        }
        return 0;
    }
}
