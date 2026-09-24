using System.Numerics;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using XiliPomodoro.Core;

namespace XiliPomodoro;
public sealed partial class MainWindow
{
    Grid timerZone = null!, timeLayer = null!, timerBlurHost = null!, timerCircle = null!;
    StackPanel timerActions = null!;
    Button pauseButton = null!;
    CompositionEffectBrush? timerBlurBrush;
    CompositionVisualSurface? timerTextSurface;
    CompositionSurfaceBrush? timerSurfaceBrush;
    SpriteVisual? timerBlurVisual;
    bool? timerActive;
    bool timerPointerInside, timerControlsShown;
    readonly HashSet<Button> hoveredTimerButtons = [];
    const double StartButtonTop = 154;
    const double TimerControlsBottom = StartButtonTop - 8;

    UIElement BuildTimer()
    {
        timerActive = null; timerPointerInside = timerControlsShown = false;
        hoveredTimerButtons.Clear();
        ReleaseTimerBlur();
        ringVisual?.Dispose(); ringShape?.StrokeBrush?.Dispose(); ringShape?.Dispose(); ringGeometry?.Dispose();
        ringVisual = null; ringShape = null; ringGeometry = null;
        var container = new Grid { Padding = new Thickness(12) };
        var stack = new StackPanel { Spacing = 16, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        stateText = Text("", 18); stateText.Foreground = accent; stateText.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        var modeBadge = new Border { MinHeight = 30, HorizontalAlignment = HorizontalAlignment.Center, Child = stateText }; stack.Children.Add(modeBadge);
        var ringGrid = timerCircle = new Grid { Width = 220, Height = 220, Background = Brush("00000000") };
        ringGrid.Children.Add(new Ellipse { Width = 216, Height = 216, Stroke = quiet, StrokeThickness = 8 });
        var ringHost = new Grid { IsHitTestVisible = false }; ringGrid.Children.Add(ringHost);
        var compositor = ElementCompositionPreview.GetElementVisual(ringHost).Compositor;
        ringGeometry = compositor.CreateEllipseGeometry(); ringGeometry.Center = new Vector2(110); ringGeometry.Radius = new Vector2(104);
        ringShape = compositor.CreateSpriteShape(ringGeometry); ringShape.StrokeThickness = 8; ringShape.StrokeBrush = compositor.CreateColorBrush(accent.Color); ringShape.StrokeStartCap = CompositionStrokeCap.Round; ringShape.StrokeEndCap = CompositionStrokeCap.Round;
        // Composition ellipse trimming begins at 12 o'clock, clockwise.
        ringVisual = compositor.CreateShapeVisual(); ringVisual.Size = new Vector2(220); ringVisual.CenterPoint = new Vector3(110, 110, 0); ringVisual.Shapes.Add(ringShape);
        ElementCompositionPreview.SetElementChildVisual(ringHost, ringVisual);
        var inside = new Canvas(); ringGrid.Children.Add(inside);
        timerZone = new Grid { Width = 208, Height = 88, Background = Brush("00000000") }; Canvas.SetLeft(timerZone, 6); Canvas.SetTop(timerZone, 66); inside.Children.Add(timerZone);
        timeLayer = new Grid { Width = 208, Height = 72, Margin = new Thickness(0, -12, 0, 0), VerticalAlignment = VerticalAlignment.Top };
        timeText = Text("25:00", 44); timeText.FontFamily = new FontFamily("Cascadia Mono, Consolas"); timeText.FontWeight = Microsoft.UI.Text.FontWeights.Light; timeText.HorizontalAlignment = HorizontalAlignment.Center;
        timeEditor = new TextBox { FontSize = 44, FontFamily = timeText.FontFamily, FontWeight = timeText.FontWeight, Foreground = ink, SelectionHighlightColor = accent, TextAlignment = TextAlignment.Center, Padding = new Thickness(0), BorderThickness = new Thickness(0), Background = Brush("00000000"), MaxLength = 6, VerticalContentAlignment = VerticalAlignment.Center };
        foreach (var key in new[] { "TextControlBackground", "TextControlBackgroundPointerOver", "TextControlBackgroundFocused", "TextControlBorderBrush", "TextControlBorderBrushPointerOver", "TextControlBorderBrushFocused" }) timeEditor.Resources[key] = Brush("00000000");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(timeEditor, "专注时长，输入分钟或分:秒");
        timeEditor.TextChanging += (_, _) => { if (!updatingTimeEditor) timeEditDirty = true; };
        // Let TextBox's native pointer handling place the caret; never select all on focus.
        timeEditor.LostFocus += (_, _) => { if (timeEditDirty && engine.State.Status == TimerStatus.Ready) CommitInlineDuration(); };
        timeEditor.KeyDown += (_, e) => { if (e.Key == Windows.System.VirtualKey.Enter) { if (CommitInlineDuration()) startButton.Focus(FocusState.Keyboard); e.Handled = true; } else if (e.Key == Windows.System.VirtualKey.Escape) { timeEditDirty = false; SyncTimeEditor(); timerHint.Text = ""; startButton.Focus(FocusState.Keyboard); e.Handled = true; } };
        timeLayer.Children.Add(timeText); timeLayer.Children.Add(timeEditor); timerZone.Children.Add(timeLayer);
        ElementCompositionPreview.SetIsTranslationEnabled(timeLayer, true);
        ElementCompositionPreview.GetElementVisual(timeLayer).CenterPoint = new Vector3(104, 36, 0);
        timerBlurHost = new Grid { IsHitTestVisible = false }; timerZone.Children.Add(timerBlurHost);
        timeText.SizeChanged += (_, _) => UpdateTimerBlurSize();
        timerActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        pauseButton = IconButton("\uE769", "暂停", ToggleTimer); pauseButton.Width = pauseButton.Height = 46; pauseButton.CornerRadius = new CornerRadius(23); pauseButton.Background = accent; pauseButton.Foreground = onAccent;
        endButton = IconButton("\uE71A", "结束专注", EndTimer); endButton.Width = endButton.Height = 46; endButton.CornerRadius = new CornerRadius(23); endButton.Background = quiet;
        timerActions.Children.Add(pauseButton); timerActions.Children.Add(endButton); timerZone.Children.Add(timerActions);
        startButton = IconButton("\uE768", "开始专注", ToggleTimer); startButton.Width = startButton.Height = 44; startButton.CornerRadius = new CornerRadius(22); startButton.Background = accent; startButton.Foreground = onAccent;
        Canvas.SetLeft(startButton, 88); Canvas.SetTop(startButton, StartButtonTop); inside.Children.Add(startButton);
        ElementCompositionPreview.GetElementVisual(startButton).CenterPoint = new Vector3(22, 22, 0);
        foreach (var button in new[] { startButton, pauseButton, endButton }) AttachTimerButtonHover(button);
        timerZone.GotFocus += (_, _) => UpdateTimerHover();
        timerZone.LostFocus += (_, _) => DispatcherQueue.TryEnqueue(() => UpdateTimerHover());
        stack.Children.Add(ringGrid);
        timerHint = Text("", 11, true); timerHint.HorizontalAlignment = HorizontalAlignment.Center; timerHint.MinHeight = 16; stack.Children.Add(timerHint);
        timerViewbox = new Viewbox { Child = stack, Stretch = Stretch.Uniform, MaxWidth = 340, MaxHeight = 400, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        container.Children.Add(timerViewbox); return container;
    }
    void EnsureTimerBlur()
    {
        if (timerBlurBrush != null) return;
        var compositor = ElementCompositionPreview.GetElementVisual(timerBlurHost).Compositor;
        try
        {
            using var effect = new GaussianBlurEffect { BlurAmount = 4, BorderMode = EffectBorderMode.Hard, Source = new CompositionEffectSourceParameter("source") };
            using var factory = compositor.CreateEffectFactory(effect);
            timerTextSurface = compositor.CreateVisualSurface();
            timerTextSurface.SourceVisual = ElementCompositionPreview.GetElementVisual(timeText);
            timerTextSurface.SourceOffset = new Vector2(-8);
            timerSurfaceBrush = compositor.CreateSurfaceBrush(timerTextSurface); timerSurfaceBrush.Stretch = CompositionStretch.Fill;
            timerBlurBrush = factory.CreateBrush(); timerBlurBrush.SetSourceParameter("source", timerSurfaceBrush);
            timerBlurVisual = compositor.CreateSpriteVisual(); timerBlurVisual.Brush = timerBlurBrush; timerBlurVisual.Opacity = 0.48f;
            UpdateTimerBlurSize();
            ElementCompositionPreview.SetElementChildVisual(timerBlurHost, timerBlurVisual);
        }
        catch (Exception ex) { ReleaseTimerBlur(); Trace("Timer blur unavailable: " + ex.Message); }
    }
    void UpdateTimerBlurSize()
    {
        if (timerTextSurface == null || timerBlurVisual == null || timeText.ActualWidth <= 0 || timeText.ActualHeight <= 0) return;
        timerTextSurface.SourceSize = new Vector2((float)timeText.ActualWidth + 16, (float)timeText.ActualHeight + 16);
        timerBlurVisual.Size = timerTextSurface.SourceSize * 1.18f;
        timerBlurVisual.Offset = new Vector3((208 - timerBlurVisual.Size.X) / 2, (88 - timerBlurVisual.Size.Y) / 2, 0);
    }
    void ReleaseTimerBlur()
    {
        if (timerBlurHost != null) ElementCompositionPreview.SetElementChildVisual(timerBlurHost, null);
        timerBlurVisual?.Dispose(); timerBlurVisual = null;
        timerBlurBrush?.Dispose(); timerBlurBrush = null;
        timerSurfaceBrush?.Dispose(); timerSurfaceBrush = null;
        timerTextSurface?.Dispose(); timerTextSurface = null;
    }
    void AttachTimerPointerHandling()
    {
        root.AddHandler(UIElement.PointerPressedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler((_, e) => DismissTimeEditorFromPointer(e.OriginalSource as DependencyObject)), true);
        root.AddHandler(UIElement.PointerMovedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler((_, e) => UpdateTimerPointer(e.GetCurrentPoint(timerCircle).Position)), true);
        timerCircle.AddHandler(UIElement.PointerEnteredEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler((_, e) => UpdateTimerPointer(e.GetCurrentPoint(timerCircle).Position)), true);
        root.PointerExited += (_, e) => { if (!Inside(root, e.GetCurrentPoint(root).Position)) { timerPointerInside = false; UpdateTimerHover(); } };
    }
    void DismissTimeEditorFromPointer(DependencyObject? source)
    {
        if (timeEditor.FocusState == FocusState.Unfocused) return;
        Control? destination = null;
        for (var node = source; node != null; node = VisualTreeHelper.GetParent(node))
        {
            if (node == timeEditor) return;
            if (destination == null && node is Control control && control.IsTabStop && control.IsEnabled)
                destination = control;
        }
        // Use a real focusable control without displaying a keyboard focus outline.
        // A non-tab-stop placeholder cannot receive programmatic focus in WinUI.
        (destination ?? startButton).Focus(FocusState.Programmatic);
        // LostFocus is queued by WinUI; apply immediately even before that notification.
        if (timeEditDirty) CommitInlineDuration();
    }
    void UpdateTimerPointer(Windows.Foundation.Point point)
    {
        var x = point.X - 110; var y = point.Y - 110;
        timerPointerInside = !hidden && !settingsOpen && !chartOpen && point.Y < TimerControlsBottom && x * x + y * y <= 110 * 110;
        UpdateTimerHover();
    }
    void RenderTimerInteraction()
    {
        var active = engine.State.Status != TimerStatus.Ready;
        var first = timerActive == null; var changed = timerActive != active;
        var keyboardStart = startButton.FocusState == FocusState.Keyboard;
        var keyboardEnd = pauseButton.FocusState == FocusState.Keyboard || endButton.FocusState == FocusState.Keyboard;
        timerActive = active;
        if (active) EnsureTimerBlur(); else ReleaseTimerBlur();
        if (active) hoveredTimerButtons.Remove(startButton);
        else { SetTimerButtonHovered(pauseButton, false); SetTimerButtonHovered(endButton, false); }
        var label = engine.State.Status == TimerStatus.Paused ? "继续" : "暂停";
        ((FontIcon)pauseButton.Content).Glyph = engine.State.Status == TimerStatus.Paused ? "\uE768" : "\uE769";
        ToolTipService.SetToolTip(pauseButton, label); Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(pauseButton, label);
        timeLayer.IsHitTestVisible = !active;
        startButton.IsHitTestVisible = startButton.IsTabStop = startButton.IsEnabled = !active;
        pauseButton.IsTabStop = pauseButton.IsEnabled = endButton.IsTabStop = endButton.IsEnabled = active;
        if (changed)
        {
            var animate = !first && Motion;
            TweenVector(timeLayer, "Translation", new Vector3(0, active ? 20 : 0, 0), animate);
            TweenVector(timeLayer, "Scale", new Vector3(active ? 1.18f : 1), animate);
            TweenOpacity(startButton, active ? 0 : 1, animate);
            RefreshTimerButtonScale(startButton, animate, 230);
            if (active && keyboardStart) pauseButton.Focus(FocusState.Keyboard);
            else if (!active && keyboardEnd) startButton.Focus(FocusState.Keyboard);
        }
        UpdateTimerHover(first);
    }
    void UpdateTimerHover(bool immediate = false)
    {
        if (timerActions == null) return;
        var show = timerActive == true && (timerPointerInside || pauseButton.FocusState == FocusState.Keyboard || endButton.FocusState == FocusState.Keyboard);
        if (!immediate && show == timerControlsShown) return;
        timerControlsShown = show;
        if (!show) { SetTimerButtonHovered(pauseButton, false); SetTimerButtonHovered(endButton, false); }
        // Preserve hit geometry when the controls appear, so crossing children never hides them.
        timerActions.IsHitTestVisible = show;
        var animate = !immediate && Motion;
        TweenOpacity(timerActions, show ? 1 : 0, animate, 130);
        TweenOpacity(timerBlurHost, show ? 1 : 0, animate, 130);
        TweenOpacity(timeLayer, show ? (timerBlurBrush != null ? 0 : 0.2f) : 1, animate, 130);
    }
    void TweenOpacity(UIElement element, float target, bool animate, int milliseconds = 230)
    {
        var visual = ElementCompositionPreview.GetElementVisual(element);
        if (!animate) { visual.StopAnimation("Opacity"); visual.Opacity = target; return; }
        var animation = visual.Compositor.CreateScalarKeyFrameAnimation(); animation.InsertExpressionKeyFrame(0, "this.StartingValue"); animation.InsertKeyFrame(1, target); animation.Duration = TimeSpan.FromMilliseconds(milliseconds); visual.StartAnimation("Opacity", animation);
    }
    void AttachTimerButtonHover(Button button)
    {
        void Center() => ElementCompositionPreview.GetElementVisual(button).CenterPoint = new Vector3((float)button.ActualWidth / 2, (float)button.ActualHeight / 2, 0);
        button.Loaded += (_, _) => Center();
        button.SizeChanged += (_, _) => Center();
        button.AddHandler(UIElement.PointerEnteredEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler((_, _) => SetTimerButtonHovered(button, true)), true);
        button.AddHandler(UIElement.PointerExitedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler((_, e) => { if (!Inside(button, e.GetCurrentPoint(button).Position)) SetTimerButtonHovered(button, false); }), true);
        button.PointerCaptureLost += (_, e) => SetTimerButtonHovered(button, Inside(button, e.GetCurrentPoint(button).Position));
        button.IsEnabledChanged += (_, _) => { if (!button.IsEnabled) SetTimerButtonHovered(button, false); };
        button.Unloaded += (_, _) => hoveredTimerButtons.Remove(button);
    }
    void SetTimerButtonHovered(Button button, bool hovered)
    {
        hovered &= button.IsEnabled && (button == startButton ? timerActive != true : timerControlsShown);
        var changed = hovered ? hoveredTimerButtons.Add(button) : hoveredTimerButtons.Remove(button);
        if (changed) RefreshTimerButtonScale(button, Motion);
    }
    void RefreshTimerButtonScale(Button button, bool animate, int milliseconds = 220)
    {
        // One scale target combines hover and the start button's existing exit animation.
        // The countdown ring, button layout and neighbouring controls never resize.
        var scale = button == startButton && timerActive == true ? 0.8f : hoveredTimerButtons.Contains(button) && Motion ? 1.14f : 1;
        TweenVector(button, "Scale", new Vector3(scale), animate, milliseconds);
    }
    void TweenVector(UIElement element, string property, Vector3 target, bool animate, int milliseconds = 230)
    {
        var visual = ElementCompositionPreview.GetElementVisual(element);
        if (!animate) { visual.StopAnimation(property); if (property == "Scale") visual.Scale = target; else visual.Properties.InsertVector3(property, target); return; }
        var animation = visual.Compositor.CreateVector3KeyFrameAnimation(); animation.InsertExpressionKeyFrame(0, "this.StartingValue"); animation.InsertKeyFrame(1, target, visual.Compositor.CreateCubicBezierEasingFunction(new Vector2(0.2f, 0), new Vector2(0.2f, 1))); animation.Duration = TimeSpan.FromMilliseconds(milliseconds); visual.StartAnimation(property, animation);
    }
}
