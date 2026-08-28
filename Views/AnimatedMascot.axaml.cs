using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;

namespace BackupManager.Views;

/// <summary>
/// An animated duck that idles in place and walks across the bottom of the screen,
/// alternating between the two behaviours. Frames are cropped from the sprite sheet
/// via a single CroppedBitmap whose integer SourceRect is updated each tick, so only
/// one 32x32 frame is ever visible (no jitter, no overlapping sprites).
/// </summary>
public partial class AnimatedMascot : UserControl
{
    private const int FrameSize = 32;
    private const int FramesIdle = 2;
    private const int FramesWalk = 6;
    private const int RenderScale = 2;      // 32px frame -> 64px on screen
    private const int RowIdle = 0;
    private const int RowWalk = 1;

    private static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan FrameIdle = TimeSpan.FromMilliseconds(180);
    private static readonly TimeSpan FrameWalk = TimeSpan.FromMilliseconds(110);
    private const double PxPerSecond = 75.0;
    private static readonly TimeSpan IdleMin = TimeSpan.FromMilliseconds(1400);
    private static readonly TimeSpan IdleMax = TimeSpan.FromMilliseconds(2600);

    private readonly Bitmap _sheet;
    private readonly CroppedBitmap _frameView;
    private readonly ScaleTransform _flip;
    private readonly DispatcherTimer _timer;

    private int _frame;
    private TimeSpan _frameTimer;
    private bool _walking;
    private int _row = RowIdle;
    private double _direction = 1;   // current movement direction
    private double _facing = 1;      // rendered facing
    private double _x;
    private double _target;
    private double _minX;
    private double _maxX;
    private double _charSize = FrameSize * RenderScale;
    private TimeSpan _idleLeft;
    private bool _initialized;

    public AnimatedMascot()
    {
        InitializeComponent();

        var uri = new Uri("avares://BackupManager/Assets/ducky_3_spritesheet.png", UriKind.Absolute);
        using var stream = AssetLoader.Open(uri);
        _sheet = new Bitmap(stream);
        _frameView = new CroppedBitmap(_sheet, new PixelRect(0, 0, FrameSize, FrameSize));
        MascotImage.Source = _frameView;

        var display = FrameSize * RenderScale;
        MascotImage.Width = display;
        MascotImage.Height = display;

        _flip = new ScaleTransform(1, 1);
        RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
        MascotImage.RenderTransform = _flip;

        _timer = new DispatcherTimer(Tick, DispatcherPriority.Background, (_, _) => OnTick());
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsVisibleProperty)
            UpdateTimer();
    }

    private void UpdateTimer() => _timer.IsEnabled = IsVisible;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _timer.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _timer.Stop();
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        var w = Math.Max(Bounds.Width, _charSize);
        _minX = 0;
        _maxX = w - _charSize;

        if (!_initialized && Bounds.Width > 0)
        {
            _initialized = true;
            _x = _minX;
            _facing = 1;
            _flip.ScaleX = _facing;
            _target = _maxX;   // initial cross goes right
            StartIdle();
        }
        else
        {
            _x = Math.Clamp(_x, _minX, _maxX);
            Place();
        }
    }

    private void StartIdle()
    {
        _row = RowIdle;
        _walking = false;
        _idleLeft = RandomIdle();
        _frame = 0;
        _frameTimer = TimeSpan.Zero;
        ShowFrame();
    }

    private void StartWalking()
    {
        _target = RandomTarget();
        _direction = _target > _x ? 1 : -1;
        if (_direction != _facing)
        {
            _facing = _direction;
            _flip.ScaleX = _facing;
        }
        _row = RowWalk;
        _walking = true;
        _frame = 0;
        _frameTimer = TimeSpan.Zero;
        ShowFrame();
    }

    /// <summary>
    /// Picks a horizontal target somewhere inside the container (with a margin and a
    /// minimum step away from the current position) so the duck doesn't only ping-pong
    /// wall-to-wall.
    /// </summary>
    private double RandomTarget()
    {
        double margin = _charSize * 0.75;
        double lo = margin;
        double hi = Math.Max(lo, _maxX - margin);
        double minStep = Math.Min(Bounds.Width * 0.15, 180.0);

        for (int i = 0; i < 8; i++)
        {
            double t = lo + (hi - lo) * Random.Shared.NextDouble();
            if (Math.Abs(t - _x) >= minStep)
                return t;
        }

        // Fallback: walk toward the far edge from the current position.
        return _x < (lo + hi) / 2 ? hi : lo;
    }

    private void OnTick()
    {
        if (_walking)
        {
            // Advance the walk animation frames.
            _frameTimer += Tick;
            while (_frameTimer >= FrameWalk)
            {
                _frameTimer -= FrameWalk;
                AdvanceFrame();
            }

            // Step toward the target edge.
            _x += _direction * (PxPerSecond * Tick.TotalSeconds);
            if ((_direction > 0 && _x >= _target) || (_direction < 0 && _x <= _target))
            {
                _x = _target;
                Place();
                StartIdle();
            }
            else
            {
                Place();
            }
            return;
        }

        // Idle animation.
        _frameTimer += Tick;
        while (_frameTimer >= FrameIdle)
        {
            _frameTimer -= FrameIdle;
            AdvanceFrame();
        }

        _idleLeft -= Tick;
        if (_idleLeft <= TimeSpan.Zero)
            StartWalking();
    }

    private void AdvanceFrame()
    {
        int count = _row == RowIdle ? FramesIdle : FramesWalk;
        _frame = (_frame + 1) % count;
        ShowFrame();
    }

    private void ShowFrame()
    {
        int count = _row == RowIdle ? FramesIdle : FramesWalk;
        int col = _frame % count;
        _frameView.SourceRect = new PixelRect(col * FrameSize, _row * FrameSize, FrameSize, FrameSize);
    }

    private void Place()
    {
        Canvas.SetLeft(Placer, _x);
        Canvas.SetTop(Placer, Bounds.Height - _charSize);
    }

    private static TimeSpan RandomIdle()
    {
        double r = Random.Shared.NextDouble();
        return IdleMin + (IdleMax - IdleMin) * r;
    }
}
