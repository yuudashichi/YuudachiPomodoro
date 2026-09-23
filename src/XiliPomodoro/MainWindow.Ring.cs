using Microsoft.UI.Composition;
using XiliPomodoro.Core;

namespace XiliPomodoro;

public sealed partial class MainWindow
{
    bool ringFilling;
    int ringGeneration;

    void SetRingProgress()
    {
        if (ringGeometry == null || ringVisual == null) return;
        var ready = engine.State.Status == TimerStatus.Ready;
        ringGeometry.TrimStart = ready ? 0 : (float)Math.Clamp(1 - engine.Remaining / engine.State.DurationSeconds, 0, 1);
        ringGeometry.TrimEnd = ready ? 0 : 1;
        // A zero-length path with round caps can otherwise leave a dot at twelve o'clock.
        ringVisual.Opacity = ready ? 0 : 1;
    }
    bool CanAnimateRing => Motion && !hidden && !settingsOpen && !chartOpen && !native.IsMinimized && engine.State.Status == TimerStatus.Running && engine.Remaining > 0;
    void AnimateRing(bool starting = false)
    {
        if (ringGeometry == null || ringVisual == null) return;
        if (ringFilling && !starting && CanAnimateRing) return;
        StopRing();
        if (!CanAnimateRing) return;
        var compositor = ringGeometry.Compositor;
        var animation = compositor.CreateScalarKeyFrameAnimation();
        if (starting)
        {
            var generation = ringGeneration;
            // Keep the end at twelve o'clock and move the start backwards around
            // the ellipse. Only the entrance fills counterclockwise.
            ringGeometry.TrimStart = ringGeometry.TrimEnd = 1;
            animation.InsertKeyFrame(0, 1);
            animation.InsertKeyFrame(1, 0, compositor.CreateCubicBezierEasingFunction(new System.Numerics.Vector2(0.2f, 0), new System.Numerics.Vector2(0.2f, 1)));
            animation.Duration = TimeSpan.FromMilliseconds(360);
            ringFilling = ringAnimating = true;
            var batch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
            batch.Completed += (_, _) =>
            {
                batch.Dispose();
                DispatcherQueue.TryEnqueue(() =>
                {
                    if (generation != ringGeneration || !ringFilling) return;
                    ringFilling = false;
                    // The clock was already running during the visual fill; catch up to it.
                    AnimateRing();
                });
            };
            ringGeometry.StartAnimation("TrimStart", animation);
            batch.End();
            return;
        }
        animation.InsertKeyFrame(0, ringGeometry.TrimStart);
        animation.InsertKeyFrame(1, 1, compositor.CreateLinearEasingFunction());
        animation.Duration = TimeSpan.FromSeconds(engine.Remaining);
        ringGeometry.StartAnimation("TrimStart", animation);
        ringAnimating = true;
    }
    void StopRing()
    {
        ringGeneration++;
        ringFilling = ringAnimating = false;
        ringGeometry?.StopAnimation("TrimStart");
        ringGeometry?.StopAnimation("TrimEnd");
        SetRingProgress();
    }
}
