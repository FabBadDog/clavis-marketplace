namespace FabioSoft.Clavis.Rendering

open System
open System.Diagnostics.CodeAnalysis
open System.Windows
open System.Windows.Media
open System.Windows.Media.Animation

/// The animation vocabulary shared across host and plugins: swift entrances, exits, fades, and crossfades
/// built only from instance-level RenderTransform + BeginAnimation. Public (the C# plugins consume it, and
/// the codebase forbids InternalsVisibleTo) and never introduces a DependencyProperty/attached property,
/// so it roots no plugin types and leaves collectible plugin AssemblyLoadContexts free to unload. Durations
/// and easings mirror the design language's motion tokens so every surface moves with one cadence.
[<ExcludeFromCodeCoverage>] // WPF animation
[<RequireQualifiedAccess>]
module Motion =

    /// The single transition duration. The design language sets a 250ms floor on every transition - the old
    /// sub-250ms tiers were imperceptible - so `Instant` and `Quick` are retained as call-site aliases but
    /// now resolve to the same 250ms Standard cadence. New code should prefer `Standard`.
    let Standard = Duration(TimeSpan.FromMilliseconds 250.0)

    /// Alias of Standard (kept so existing call sites compile). Was 80ms; now 250ms per the motion floor.
    let Instant = Standard

    /// Alias of Standard (kept so existing call sites compile). Was 140ms; now 250ms per the motion floor.
    let Quick = Standard

    /// The slide-up distance for an entrance, in device-independent pixels.
    [<Literal>]
    let private enterOffset = 8.0

    /// The scale a tile collapses to as it leaves.
    [<Literal>]
    let private leaveScale = 0.96

    let easeOut () =
        CubicEase(EasingMode = EasingMode.EaseOut) :> IEasingFunction

    let easeInOut () =
        CubicEase(EasingMode = EasingMode.EaseInOut) :> IEasingFunction

    let private doubleAnimation (from: float) (toValue: float) (duration: Duration) =
        DoubleAnimation(from, toValue, duration, EasingFunction = easeOut())

    // A two-transform group (scale then translate, centred) so entrances and exits can move and scale the
    // same element without one clobbering the other's RenderTransform. Reused if already present.
    let private ensureGroup (element: FrameworkElement) =
        match element.RenderTransform with
        | :? TransformGroup as group when group.Children.Count = 2 -> group
        | _ ->
            let group = TransformGroup()
            group.Children.Add(ScaleTransform())
            group.Children.Add(TranslateTransform())
            element.RenderTransform <- group
            element.RenderTransformOrigin <- Point(0.5, 0.5)
            group

    let private translateOf (group: TransformGroup) = group.Children[1] :?> TranslateTransform

    let private scaleOf (group: TransformGroup) = group.Children[0] :?> ScaleTransform

    /// Fade an element's opacity to a target value (quick). The workhorse behind hover reveals and the
    /// drop-hint show/hide.
    let fadeTo (element: UIElement) (target: float) =
        element.BeginAnimation(UIElement.OpacityProperty, DoubleAnimation(target, Quick, EasingFunction = easeOut()))

    /// Start hidden, then fade in (quick). Used for tab content on switch and the drop hint appearing.
    let appear (element: UIElement) =
        element.Opacity <- 0.0
        fadeTo element 1.0

    /// Fade an element out (quick), then run onDone. Lets a caller defer removal until the fade finishes.
    let disappear (element: UIElement) (onDone: Action) =
        let animation = DoubleAnimation(0.0, Quick, EasingFunction = easeOut())
        animation.Completed.Add(fun _ -> onDone.Invoke())
        element.BeginAnimation(UIElement.OpacityProperty, animation)

    /// Entrance: fade in while sliding up into place (standard). Used for a tile docking in.
    let enter (element: FrameworkElement) =
        let group = ensureGroup element
        let translate = translateOf group
        element.Opacity <- 0.0
        translate.Y <- enterOffset
        element.BeginAnimation(UIElement.OpacityProperty, doubleAnimation 0.0 1.0 Standard)
        translate.BeginAnimation(TranslateTransform.YProperty, doubleAnimation enterOffset 0.0 Standard)

    /// Exit: fade out while collapsing slightly (quick), then run onDone (the model removal + rebuild).
    let leave (element: FrameworkElement) (onDone: Action) =
        let group = ensureGroup element
        let scale = scaleOf group
        let fade = DoubleAnimation(1.0, 0.0, Quick, EasingFunction = easeOut())
        fade.Completed.Add(fun _ -> onDone.Invoke())
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, doubleAnimation 1.0 leaveScale Quick)
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, doubleAnimation 1.0 leaveScale Quick)
        element.BeginAnimation(UIElement.OpacityProperty, fade)

    /// Crossfade: fade the outgoing element out and the incoming one in together (quick).
    let crossfade (outgoing: UIElement) (incoming: UIElement) =
        fadeTo outgoing 0.0
        appear incoming

    /// Fade a whole window to a target opacity (quick), then run onDone - secondary windows open and close
    /// with this. Windows are AllowsTransparency, so opacity animates cleanly.
    let fadeWindow (window: Window) (target: float) (onDone: Action) =
        let animation = DoubleAnimation(window.Opacity, target, Quick, EasingFunction = easeOut())
        if not (isNull onDone) then
            animation.Completed.Add(fun _ -> onDone.Invoke())
        window.BeginAnimation(UIElement.OpacityProperty, animation)

    // A gravity drop: an accelerating fall (slow -> fast, like real gravity) from an explicit start to
    // the floor, then a small bounce up and settle. Built from spline keyframes because a single easing
    // function cannot both accelerate into a landing and bounce out of it. The start is a discrete 0%
    // keyframe rather than the property's current value, so the caller never has to overwrite the local
    // value to position the start.
    let private fallKeyframes (fromY: float) (target: float) (durationMs: float) =
        let frames = DoubleAnimationUsingKeyFrames(Duration = Duration(TimeSpan.FromMilliseconds durationMs))
        let accelerate = KeySpline(0.4, 0.0, 0.9, 0.45)  // flat start, steep finish -> accelerating fall
        let easeUp = KeySpline(0.1, 0.6, 0.4, 1.0)
        let settle = KeySpline(0.4, 0.0, 0.2, 1.0)
        frames.KeyFrames.Add(DiscreteDoubleKeyFrame(fromY, KeyTime.FromPercent 0.0)) |> ignore
        frames.KeyFrames.Add(SplineDoubleKeyFrame(target, KeyTime.FromPercent 0.72, KeySpline = accelerate)) |> ignore
        frames.KeyFrames.Add(SplineDoubleKeyFrame(target - 12.0, KeyTime.FromPercent 0.86, KeySpline = easeUp)) |> ignore
        frames.KeyFrames.Add(SplineDoubleKeyFrame(target, KeyTime.FromPercent 1.0, KeySpline = settle)) |> ignore
        frames

    /// How far above its resting place a window starts its drop, beyond its own height (so it begins fully
    /// off the top of the screen).
    [<Literal>]
    let private fallClearance = 40.0

    // The resting (local, non-animated) Top. While a Top animation is in flight, Window.Top reads the
    // animated value, so capturing that as a start or target would corrupt the window's resting place;
    // the local base value is safe because no entrance/exit here ever overwrites it mid-flight.
    let private restingTop (window: Window) =
        match window.ReadLocalValue(Window.TopProperty) with
        | :? float as top -> top
        | _ -> window.Top

    let private fallInCore (window: Window) (fromY: float) (target: float) (onDone: Action) =
        let frames = fallKeyframes fromY target 720.0
        frames.Completed.Add(fun _ ->
            window.BeginAnimation(Window.TopProperty, null)
            window.Top <- target
            if not (isNull onDone) then
                onDone.Invoke())
        window.BeginAnimation(Window.TopProperty, frames)

    /// Gravity drop-in for an already-visible window: it falls from above the screen to its resting Top,
    /// accelerating and settling with a small bounce. The animation is released on completion so the
    /// window stays draggable.
    let fallInWindow (window: Window) =
        fallInCore window (-(window.ActualHeight + fallClearance)) (restingTop window) null

    /// Show a hidden window with the gravity drop-in, without flashing it at its resting place first: a
    /// previously-shown window's composed surface is otherwise presented at its old position the instant
    /// it becomes visible, before any animation tick can move it. The window is parked above the screen
    /// by a synchronous position write while still hidden, then shown and dropped to its resting Top
    /// (captured before parking and restored as the local value on completion), then onDone runs.
    let showWindowFallingIn (window: Window) (onDone: Action) =
        let resting = restingTop window
        let fromY = -(window.ActualHeight + fallClearance)
        window.Top <- fromY
        window.Show()
        fallInCore window fromY resting onDone

    /// Free-fall a window out the bottom of the screen (accelerating, no bounce), then run onDone - the
    /// startup splash uses this to drop away once boot completes.
    let fallOutWindow (window: Window) (onDone: Action) =
        let gravity = PowerEase(EasingMode = EasingMode.EaseIn, Power = 2.4)
        let target = SystemParameters.PrimaryScreenHeight + window.ActualHeight
        let animation = DoubleAnimation(window.Top, target, Duration(TimeSpan.FromMilliseconds 600.0), EasingFunction = gravity)
        if not (isNull onDone) then
            animation.Completed.Add(fun _ -> onDone.Invoke())
        window.BeginAnimation(Window.TopProperty, animation)

    /// Launch a window up and out the top of the screen (accelerating, the reverse of fallInWindow), then
    /// run onDone - typically Hide. The resting Top is captured from the local (non-animated) value and
    /// restored on completion, so the saved bounds stay correct and the next fall-in lands where the
    /// window belongs.
    let riseOutWindow (window: Window) (onDone: Action) =
        let resting = restingTop window
        let gravity = PowerEase(EasingMode = EasingMode.EaseIn, Power = 2.4)
        let target = -(window.ActualHeight + fallClearance)
        let animation = DoubleAnimation(window.Top, target, Duration(TimeSpan.FromMilliseconds 450.0), EasingFunction = gravity)
        animation.Completed.Add(fun _ ->
            if not (isNull onDone) then
                onDone.Invoke()
            window.BeginAnimation(Window.TopProperty, null)
            window.Top <- resting)
        window.BeginAnimation(Window.TopProperty, animation)
