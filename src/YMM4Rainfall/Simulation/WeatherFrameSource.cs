// SPDX-License-Identifier: MPL-2.0
using System.Numerics;

namespace YMM4Rainfall.Simulation;

/// <summary>
/// 同じ入力範囲、設定、尺、FPS、フレームから、評価順に依存しない天候粒子を生成します。
/// </summary>
internal sealed class WeatherFrameSource
{
    private const int CheckpointFrames = 120;
    private const int MinimumUpdraftSubsteps = 4;
    private const int MaximumUpdraftSubsteps = 16;
    private const long MaximumLocalCacheBytes = 32L * 1024 * 1024;
    private const double FullHdArea = 1920.0 * 1080.0;

    private readonly object sync = new();
    private readonly CommonMotion commonMotion = new();
    private readonly SortedDictionary<long, LocalOffsets> localCheckpoints = [];
    private string? definition;
    private RainfallBounds cachedBounds;
    private long cachedDuration;
    private int cachedFps;
    private Func<long, RainfallParameters>? parametersAt;
    private Particle[] particles = [];
    private bool hasLocalMotion;
    private bool hasOnset;
    private long latestLocalFrame = -1;
    private LocalOffsets? latestLocalOffsets;
    private double margin;
    private double spanX;
    private double spanY;
    private bool diagonalOnset;
    private double domainCos = 1;
    private double domainSin;
    private double domainWidth;
    private double domainHeight;
    private WeatherVector domainCenter;

    /// <summary>
    /// 指定フレームを評価します。parametersAt は同じ signature の間、同じフレームへ同じ値を返す必要があります。
    /// </summary>
    public RainfallStroke[] Evaluate(
        RainfallBounds bounds,
        long frame,
        long duration,
        int fps,
        string signature,
        Func<long, RainfallParameters> parametersAt)
    {
        ArgumentNullException.ThrowIfNull(parametersAt);
        if (!bounds.IsValid || fps <= 0 || duration < 0) return [];
        frame = Math.Clamp(frame, 0, duration);

        lock (sync)
        {
            var nextDefinition = string.Create(System.Globalization.CultureInfo.InvariantCulture,
                $"{signature}\u001f{bounds.Left:R},{bounds.Top:R},{bounds.Right:R},{bounds.Bottom:R}\u001f{duration}\u001f{fps}");
            if (!string.Equals(definition, nextDefinition, StringComparison.Ordinal))
                Reset(nextDefinition, bounds, duration, fps, parametersAt);
            else
                this.parametersAt = parametersAt;

            var parameters = GetParameters(frame);
            if (parameters.Amount == 0 || parameters.Opacity == 0) return [];
            if (parameters.OnsetEnabled && frame / (double)fps < parameters.StartSeconds) return [];

            var count = GetParticleCount(bounds, parameters.Amount);
            EnsureParticleCapacity(count);
            MotionSample? motionSample = null;
            if (parameters.HasMotionBlur && duration > 0)
            {
                // 前フレームを先に評価すると、通常再生時に局所移動の積分キャッシュを再利用できます。
                var sampleFrame = frame > 0 ? frame - 1 : 1;
                motionSample = new(GetParameters(sampleFrame), sampleFrame / (double)fps,
                    commonMotion.Evaluate(sampleFrame, fps, GetParameters), GetLocalOffsets(sampleFrame));
            }
            var common = commonMotion.Evaluate(frame, fps, GetParameters);
            var local = GetLocalOffsets(frame);
            var seconds = frame / (double)fps;
            var strokes = new List<RainfallStroke>(count);
            for (var index = 0; index < count; index++)
            {
                if (parameters.OnsetEnabled &&
                    (seconds < particles[index].BirthSeconds || !local.Released[index])) continue;
                strokes.Add(CreateStroke(index, parameters, seconds, common, local, motionSample));
            }
            return strokes.ToArray();
        }
    }

    private void Reset(
        string nextDefinition,
        RainfallBounds bounds,
        long duration,
        int fps,
        Func<long, RainfallParameters> parametersAt)
    {
        definition = nextDefinition;
        cachedBounds = bounds;
        cachedDuration = duration;
        cachedFps = fps;
        this.parametersAt = parametersAt;
        particles = [];
        localCheckpoints.Clear();
        latestLocalFrame = -1;
        latestLocalOffsets = null;
        commonMotion.Reset();

        var initial = GetParameters(0);
        hasLocalMotion = initial.Kind == WeatherKind.Snow && initial.SnowMotion != SnowMotionKind.Basic;
        hasOnset = initial.OnsetEnabled;
        margin = initial.Shape == RainfallShape.Streak
            ? RainfallParameters.MaximumLength * 1.4 + RainfallParameters.MaximumThickness * 2
            : (initial.Shape == RainfallShape.Png && initial.PngSourceSize > 0
                ? initial.ParticleSizeLimit : RainfallParameters.MaximumParticleSize) * 2 + 2;
        spanX = bounds.Width + margin * 2;
        spanY = bounds.Height + margin * 2;
        domainCenter = new((bounds.Left + bounds.Right) / 2, (bounds.Top + bounds.Bottom) / 2);
        domainWidth = bounds.Width;
        domainHeight = bounds.Height;
        domainCos = 1;
        domainSin = 0;
        var entryFrame = Math.Clamp((long)Math.Floor(initial.StartSeconds * fps), 0, duration);
        var entry = GetParameters(entryFrame);
        var entryVelocity = CommonMotion.GetVelocity(entry, entryFrame / (double)fps);
        var flow = entryVelocity.Base + entryVelocity.Wind;
        diagonalOnset = hasOnset && (initial.Kind is WeatherKind.Rain or WeatherKind.Snow) &&
            Math.Abs(flow.X) > 1e-6 && Math.Abs(flow.Y) > 1e-6;
        if (diagonalOnset)
        {
            var speed = Math.Sqrt(flow.LengthSquared);
            domainCos = flow.Y / speed;
            domainSin = flow.X / speed;
            domainWidth = Math.Abs(domainCos) * bounds.Width + Math.Abs(domainSin) * bounds.Height;
            domainHeight = Math.Abs(domainSin) * bounds.Width + Math.Abs(domainCos) * bounds.Height;
            spanX = domainWidth + margin * 2;
            spanY = domainHeight + margin * 2;
        }
    }

    private RainfallParameters GetParameters(long frame)
        => parametersAt!(Math.Clamp(frame, 0, cachedDuration)).Normalize();

    private int GetParticleCount(RainfallBounds bounds, double amount)
    {
        var shape = GetParameters(0).Shape;
        var populationArea = shape == RainfallShape.Streak ? bounds.Width * bounds.Height : spanX * spanY;
        if (diagonalOnset && shape == RainfallShape.Streak)
            populationArea *= spanX * spanY / ((bounds.Width + margin * 2) * (bounds.Height + margin * 2));
        return Math.Clamp((int)Math.Ceiling(populationArea / FullHdArea * amount * 10), 1, RainfallSimulation.MaximumStrokeCount);
    }

    private void EnsureParticleCapacity(int count)
    {
        if (particles.Length >= count) return;
        var expanded = new Particle[count];
        Array.Copy(particles, expanded, particles.Length);
        for (var index = particles.Length; index < count; index++) expanded[index] = CreateParticle(index);
        particles = expanded;
        // 配列長が変わると保存済みオフセットの長さも変わるため、局所積分だけを再計算します。
        localCheckpoints.Clear();
        localCheckpoints[0] = CreateInitialState();
        latestLocalFrame = -1;
        latestLocalOffsets = null;
    }

    private Particle CreateParticle(int index)
    {
        var initial = GetParameters(0);
        var speedRandom = WeatherFields.UnitRandom(index, initial.Seed, 2) * 2 - 1;
        var lengthScale = 0.6 + WeatherFields.UnitRandom(index, initial.Seed, 33) * 0.8;
        var sizeRandom = WeatherFields.UnitRandom(index, initial.Seed, 34) * 2 - 1;
        var windRandom = WeatherFields.UnitRandom(index, initial.Seed, 21) * 2 - 1;
        if (initial.RainSizeMotionLinked)
        {
            // 基本速度は全種類で相対サイズへ合わせ、風の逆相関は雨だけに保ちます。
            speedRandom = sizeRandom;
            if (initial.Kind == WeatherKind.Rain) windRandom = -sizeRandom;
        }
        var randomBirth = WeatherFields.UnitRandom(index, initial.Seed, 4);
        // 最初の1粒だけはランダムな待機と周回待ちを省き、開始を画面端から知らせます。
        // 立ち上がり0秒の分散配置と、残りの粒の発生時刻は変更しません。
        var leadParticle = index == 0 && initial.OnsetEnabled &&
            (initial.RampEnabled ? initial.RampSeconds : initial.AppearanceSeconds) > 0;
        var birth = initial.OnsetEnabled
            ? initial.StartSeconds + (leadParticle ? 0 : initial.RampEnabled
                ? Math.Sqrt(randomBirth) * initial.RampSeconds
                : randomBirth * initial.AppearanceSeconds)
            : 0;
        var birthFrame = Math.Clamp((long)Math.Floor(birth * cachedFps), 0, cachedDuration);
        var birthParameters = GetParameters(birthFrame);
        var durationSeconds = cachedDuration / (double)cachedFps;
        var birthCommon = birth <= durationSeconds
            ? commonMotion.EvaluateSeconds(birth, cachedFps, GetParameters)
            : default;

        // 出現開始でも通常時の分散配置を保ち、短い立ち上がりで全粒を画面端へ集めません。
        var originX = cachedBounds.Left - margin + WeatherFields.UnitRandom(index, initial.Seed, 0) * spanX;
        var originY = cachedBounds.Top - margin + WeatherFields.UnitRandom(index, initial.Seed, 1) * spanY;
        if (diagonalOnset)
        {
            var origin = FromDomain(new(
                (WeatherFields.UnitRandom(index, initial.Seed, 0) - 0.5) * spanX,
                (WeatherFields.UnitRandom(index, initial.Seed, 1) - 0.5) * spanY));
            originX = origin.X;
            originY = origin.Y;
        }
        if (leadParticle)
        {
            var origin = LeadParticleOrigin(birthParameters, birth, speedRandom, sizeRandom, windRandom);
            originX = origin.X;
            originY = origin.Y;
        }
        var startsOutside = leadParticle || !initial.OnsetEnabled || IsOutsideInput(
            new(originX, originY), birthParameters, sizeRandom, lengthScale);
        return new(index, speedRandom, sizeRandom, lengthScale, windRandom, birth, birthCommon,
            originX, originY, startsOutside);
    }

    private WeatherVector LeadParticleOrigin(
        RainfallParameters parameters, double birth, double speedRandom, double sizeRandom, double windRandom)
    {
        var velocity = CommonVelocity(parameters, birth, speedRandom, windRandom);
        var stationary = velocity.LengthSquared < 1e-12;
        if (stationary)
            velocity = WeatherFields.Direction(1, BaseAngle(parameters));
        var scale = 1 + sizeRandom * parameters.ThicknessVariation / 100;
        var rotation = parameters.RotationAngle + WeatherFields.UnitRandom(0, parameters.Seed, 40) * 360
            * parameters.InitialRotationVariation
            / 100 - (parameters.FollowDirection ? WeatherFields.DirectionAngle(velocity, BaseAngle(parameters)) : 0);
        var radians = rotation * Math.PI / 180;
        // 水滴は縦長の外形と先端の輪郭を含む円で余白を取り、停止中の回転にも備えます。
        // PNGは開始時の回転後の四隅まで含めます。線は頭から入るため太さだけを使います。
        var radius = parameters.Shape == RainfallShape.Streak
            ? Math.Max(0.01, parameters.Thickness * scale) / 2
            : Math.Max(0.01, parameters.ParticleSize * scale) *
                (parameters.Shape switch
                {
                    RainfallShape.CartoonDrop => 1,
                    RainfallShape.Png => stationary ? Math.Sqrt(0.5)
                        : (Math.Abs(Math.Cos(radians)) + Math.Abs(Math.Sin(radians))) / 2,
                    _ => 0.5,
                });
        var padding = radius + 1;
        var across = 0.2 + WeatherFields.UnitRandom(0, parameters.Seed, 0) * 0.6;
        if (Math.Abs(velocity.Y) >= Math.Abs(velocity.X))
            return new(cachedBounds.Left + cachedBounds.Width * across,
                velocity.Y >= 0 ? cachedBounds.Top - padding : cachedBounds.Bottom + padding);
        return new(velocity.X >= 0 ? cachedBounds.Left - padding : cachedBounds.Right + padding,
            cachedBounds.Top + cachedBounds.Height * across);
    }

    private RainfallStroke CreateStroke(
        int index,
        RainfallParameters parameters,
        double seconds,
        CommonTravel common,
        LocalOffsets local,
        MotionSample? motionSample)
    {
        var particle = particles[index];
        var age = parameters.OnsetEnabled ? seconds - particle.BirthSeconds : seconds;
        var drift = WeatherFields.Drift(parameters, index, age, parameters.OnsetEnabled);
        var sway = WeatherFields.Sway(parameters, index, age, parameters.OnsetEnabled);
        var displacement = ParticleCommon(common, particle);
        if (parameters.OnsetEnabled) displacement -= ParticleCommon(particle.BirthCommon, particle);
        var rawX = particle.OriginX + displacement.X + drift.Offset.X + sway.Offset.X + local.X[index];
        var rawY = particle.OriginY + displacement.Y + drift.Offset.Y + sway.Offset.Y + local.Y[index];
        var position = WrapPosition(new(rawX, rawY));
        var x = position.X;
        var y = position.Y;

        // 既存の水泡・PNGは揺らめきに画像の向きを追従させません。
        // 雪の漂いは新仕様の合成速度に含め、粒の追従方向へ反映します。
        var velocity = CommonVelocity(parameters, seconds, particle.SpeedRandom, particle.WindRandom)
            + drift.Velocity;
        velocity += LocalVelocity(parameters, x, y);
        var directionAngle = WeatherFields.DirectionAngle(velocity, BaseAngle(parameters));
        var direction = WeatherFields.Direction(1, directionAngle);
        var length = parameters.Length * particle.LengthScale;
        var thickness = parameters.Thickness
            * (1 + particle.SizeRandom * parameters.ThicknessVariation / 100);
        var size = parameters.ParticleSize
            * (1 + particle.SizeRandom * parameters.ThicknessVariation / 100);
        var head = new Vector2((float)x, (float)y);
        var tail = new Vector2((float)(x - direction.X * length), (float)(y - direction.Y * length));
        var opacity = parameters.Opacity / 100
            * (1 - WeatherFields.UnitRandom(index, parameters.Seed, 3) * parameters.OpacityVariation / 100);

        var initialRotation = WeatherFields.UnitRandom(index, parameters.Seed, 40) * 360
            * parameters.InitialRotationVariation / 100;
        var spinFactor = 1 + (WeatherFields.UnitRandom(index, parameters.Seed, 41) * 2 - 1)
            * parameters.RotationSpeedVariation / 100;
        var spinSpeed = parameters.RotationSpeed * spinFactor;
        var spin = spinSpeed == 0 ? 0 : (age % (360 / Math.Abs(spinSpeed))) * spinSpeed;
        var rotation = (parameters.RotationAngle + initialRotation + spin
            - (parameters.FollowDirection ? directionAngle : 0)) % 360;
        var flipHorizontal = false;
        if (parameters.KeepUpright && parameters.Shape != RainfallShape.Streak)
        {
            rotation = (rotation + 540) % 360 - 180;
            if (rotation > 90) { rotation -= 180; flipHorizontal = parameters.MirrorWhenUpright; }
            else if (rotation < -90) { rotation += 180; flipHorizontal = parameters.MirrorWhenUpright; }
        }

        var variant = RainfallShapeVariants.Select(parameters, WeatherFields.UnitRandom(index, parameters.Seed, 50));
        var blurVelocity = default(Vector2);
        if (parameters.HasMotionBlur)
        {
            var actualVelocity = velocity + sway.Velocity;
            if (motionSample is { } sample && (!parameters.OnsetEnabled || sample.Seconds >= particle.BirthSeconds))
            {
                var sampleAge = parameters.OnsetEnabled ? sample.Seconds - particle.BirthSeconds : sample.Seconds;
                var sampleTravel = ParticleCommon(sample.Common, particle);
                if (parameters.OnsetEnabled) sampleTravel -= ParticleCommon(particle.BirthCommon, particle);
                var sampleDrift = WeatherFields.Drift(sample.Parameters, index, sampleAge, parameters.OnsetEnabled).Offset;
                var sampleSway = WeatherFields.Sway(sample.Parameters, index, sampleAge, parameters.OnsetEnabled).Offset;
                var sampleX = particle.OriginX + sampleTravel.X + sampleDrift.X + sampleSway.X + sample.Local.X[index];
                var sampleY = particle.OriginY + sampleTravel.Y + sampleDrift.Y + sampleSway.Y + sample.Local.Y[index];
                actualVelocity = new WeatherVector(rawX - sampleX, rawY - sampleY) / (seconds - sample.Seconds);
            }
            if (double.IsFinite(actualVelocity.X) && double.IsFinite(actualVelocity.Y))
                blurVelocity = new((float)actualVelocity.X, (float)actualVelocity.Y);
        }
        return new(tail, head, (float)Math.Max(0.01, thickness), (float)opacity,
            (float)Math.Max(0.01, size), (float)rotation, flipHorizontal, variant)
        { BlurVelocity = blurVelocity };
    }

    private sealed record MotionSample(RainfallParameters Parameters, double Seconds, CommonTravel Common, LocalOffsets Local);

    private LocalOffsets GetLocalOffsets(long frame)
    {
        if (!localCheckpoints.TryGetValue(0, out _))
            localCheckpoints[0] = CreateInitialState();
        if (!hasLocalMotion && !hasOnset) return localCheckpoints[0];

        var startFrame = 0L;
        LocalOffsets? checkpoint = null;
        foreach (var pair in localCheckpoints)
        {
            if (pair.Key > frame) break;
            startFrame = pair.Key;
            checkpoint = pair.Value;
        }
        if (latestLocalOffsets is not null && latestLocalFrame >= startFrame && latestLocalFrame <= frame)
        {
            startFrame = latestLocalFrame;
            checkpoint = latestLocalOffsets;
        }
        checkpoint ??= localCheckpoints[0];
        if (startFrame == frame || (!hasLocalMotion && checkpoint.PendingReleaseCount == 0))
        {
            // 放出済みで局所移動もなければ、以後の座標は共通移動だけで評価できます。
            latestLocalFrame = frame;
            latestLocalOffsets = checkpoint;
            return checkpoint;
        }
        var result = checkpoint.Clone();
        while (startFrame < frame)
        {
            if (!hasLocalMotion && result.PendingReleaseCount == 0) break;
            var framesToBoundary = CheckpointFrames - startFrame % CheckpointFrames;
            var endFrame = startFrame + Math.Min(framesToBoundary, frame - startFrame);
            IntegrateLocal(result, startFrame, endFrame);
            startFrame = endFrame;
            if (startFrame % CheckpointFrames == 0) StoreLocalCheckpoint(startFrame, result);
        }
        latestLocalFrame = frame;
        latestLocalOffsets = result;
        return result;
    }

    private void IntegrateLocal(LocalOffsets offsets, long from, long to)
    {
        var common = commonMotion.Evaluate(from, cachedFps, GetParameters);
        var parameters = GetParameters(from);
        var velocity = CommonMotion.GetVelocity(parameters, from / (double)cachedFps);
        for (var frame = from; frame < to; frame++)
        {
            var nextParameters = GetParameters(frame + 1);
            var nextVelocity = CommonMotion.GetVelocity(nextParameters, (frame + 1) / (double)cachedFps);
            var nextCommon = common + (velocity + nextVelocity) * (0.5 / cachedFps);
            var startSeconds = frame / (double)cachedFps;
            var endSeconds = (frame + 1) / (double)cachedFps;
            var frameSubsteps = GetLocalSubstepCount(parameters, nextParameters);
            for (var index = 0; index < particles.Length; index++)
            {
                var particle = particles[index];
                var pendingRelease = hasOnset && !offsets.Released[index];
                if (!hasLocalMotion && !pendingRelease) continue;
                if (endSeconds <= particle.BirthSeconds) continue;
                if (!hasLocalMotion && nextCommon == common &&
                    !parameters.DriftEnabled && !nextParameters.DriftEnabled &&
                    !parameters.SwayEnabled && !nextParameters.SwayEnabled &&
                    parameters.ParticleSize == nextParameters.ParticleSize &&
                    parameters.ThicknessVariation == nextParameters.ThicknessVariation &&
                    parameters.Thickness == nextParameters.Thickness && parameters.Length == nextParameters.Length)
                    continue;
                var intervalStart = Math.Max(startSeconds, particle.BirthSeconds);
                var dt = endSeconds - intervalStart;
                var substeps = Math.Max(1, (int)Math.Ceiling(frameSubsteps * dt * cachedFps));
                var substepSeconds = dt / substeps;
                var startCommon = intervalStart == startSeconds
                    ? common
                    : commonMotion.EvaluateSeconds(intervalStart, cachedFps, GetParameters);
                var startParameters = intervalStart == startSeconds
                    ? parameters
                    : GetParameters((long)Math.Floor(intervalStart * cachedFps));
                var unwrappedStart = pendingRelease
                    ? UnwrappedParticlePosition(index, particle, startParameters, intervalStart, startCommon, offsets)
                    : default;
                var startPosition = pendingRelease ? WrapPosition(unwrappedStart)
                    : ParticlePosition(index, particle, startParameters, intervalStart, startCommon, offsets);
                if (hasLocalMotion)
                {
                    var endBase = ParticlePosition(index, particle, nextParameters, endSeconds, nextCommon, offsets);
                    var difference = ToDomainVector(endBase - startPosition);
                    var baseStep = FromDomainVector(new(
                        WrappedDifference(difference.X, spanX) / substeps,
                        WrappedDifference(difference.Y, spanY) / substeps));
                    var currentPosition = startPosition;
                    for (var substep = 0; substep < substeps; substep++)
                    {
                        var startFraction = Math.Clamp((intervalStart - startSeconds + substep * substepSeconds) * cachedFps, 0, 1);
                        var endFraction = Math.Clamp((intervalStart - startSeconds + (substep + 1) * substepSeconds) * cachedFps, 0, 1);
                        var first = LocalVelocity(parameters, nextParameters, startFraction, currentPosition.X, currentPosition.Y);
                        var predicted = WrapPosition(currentPosition + baseStep + first * substepSeconds);
                        var last = LocalVelocity(parameters, nextParameters, endFraction, predicted.X, predicted.Y);
                        var localStep = (first + last) * (0.5 * substepSeconds);
                        offsets.X[index] += localStep.X;
                        offsets.Y[index] += localStep.Y;
                        var unwrappedNext = currentPosition + baseStep + localStep;
                        if (pendingRelease && IsOutsideInput(unwrappedNext, nextParameters, particle.SizeRandom, particle.LengthScale))
                            offsets.Release(index);
                        currentPosition = WrapPosition(unwrappedNext);
                    }
                }
                // 画面内で待機していた粒は、完全に画面外へ出てから同じ軌道のまま表示します。
                // 折り返し前の移動も見るため、1フレームで外周を越えても放出履歴を失いません。
                if (pendingRelease)
                {
                    var unwrappedEnd = UnwrappedParticlePosition(index, particle, nextParameters, endSeconds, nextCommon, offsets);
                    if (IsOutsideInput(startPosition, startParameters, particle.SizeRandom, particle.LengthScale) ||
                        IsOutsideInput(WrapPosition(unwrappedEnd), nextParameters, particle.SizeRandom, particle.LengthScale) ||
                        IsOutsideInput(startPosition + unwrappedEnd - unwrappedStart, nextParameters, particle.SizeRandom, particle.LengthScale))
                        offsets.Release(index);
                }
            }
            common = nextCommon;
            parameters = nextParameters;
            velocity = nextVelocity;
        }
    }

    private WeatherVector ParticlePosition(
        int index,
        Particle particle,
        RainfallParameters parameters,
        double seconds,
        CommonTravel common,
        LocalOffsets offsets)
    {
        var displacement = ParticleCommon(common, particle);
        if (parameters.OnsetEnabled) displacement -= ParticleCommon(particle.BirthCommon, particle);
        var drift = WeatherFields.Drift(parameters, index, seconds - particle.BirthSeconds, parameters.OnsetEnabled).Offset;
        var rawX = particle.OriginX + displacement.X + drift.X + offsets.X[index];
        var rawY = particle.OriginY + displacement.Y + drift.Y + offsets.Y[index];
        return WrapPosition(new(rawX, rawY));
    }

    private WeatherVector UnwrappedParticlePosition(
        int index, Particle particle, RainfallParameters parameters, double seconds,
        CommonTravel common, LocalOffsets offsets)
    {
        var displacement = ParticleCommon(common, particle) - ParticleCommon(particle.BirthCommon, particle);
        var age = seconds - particle.BirthSeconds;
        var drift = WeatherFields.Drift(parameters, index, age, true).Offset;
        var sway = WeatherFields.Sway(parameters, index, age, true).Offset;
        return new(particle.OriginX + displacement.X + drift.X + sway.X + offsets.X[index],
            particle.OriginY + displacement.Y + drift.Y + sway.Y + offsets.Y[index]);
    }

    private bool IsOutsideInput(
        WeatherVector position, RainfallParameters parameters, double sizeRandom, double lengthScale)
    {
        var scale = 1 + sizeRandom * parameters.ThicknessVariation / 100;
        var padding = parameters.Shape == RainfallShape.Streak
            ? parameters.Length * lengthScale + Math.Max(0.01, parameters.Thickness * scale) + 1
            : Math.Max(0.01, parameters.ParticleSize * scale) + 1;
        // 斜めの開始では、画面を囲む進行方向に直交した面の外で放出します。
        // 横端を別の入口にせず、循環領域も同じ向きにして流れを保ちます。
        if (diagonalOnset)
            return Math.Abs(ToDomainVector(position - domainCenter).Y) > domainHeight / 2 + padding;
        return position.X < cachedBounds.Left - padding || position.X > cachedBounds.Right + padding ||
            position.Y < cachedBounds.Top - padding || position.Y > cachedBounds.Bottom + padding;
    }

    private LocalOffsets CreateInitialState() => new(
        new double[particles.Length], new double[particles.Length],
        particles.Select(particle => particle.StartsOutside).ToArray());

    private WeatherVector LocalVelocity(RainfallParameters parameters, double x, double y)
        => parameters.Kind != WeatherKind.Snow ? default
            : parameters.SnowMotion switch
            {
                SnowMotionKind.Vortex => WeatherFields.VortexVelocity(cachedBounds, parameters, x, y),
                SnowMotionKind.Updraft => WeatherFields.UpdraftVelocity(cachedBounds, parameters, x, y),
                _ => default,
            };

    private WeatherVector LocalVelocity(
        RainfallParameters start, RainfallParameters end, double fraction, double x, double y)
    {
        var first = LocalVelocity(start, x, y);
        if (fraction <= 0) return first;
        var last = LocalVelocity(end, x, y);
        return fraction >= 1 ? last : first * (1 - fraction) + last * fraction;
    }

    private int GetLocalSubstepCount(RainfallParameters start, RainfallParameters end)
    {
        if (start.SnowMotion != SnowMotionKind.Updraft && end.SnowMotion != SnowMotionKind.Updraft)
            return 1;

        var minimumDimension = Math.Min(
            Math.Min(start.UpdraftWidth, start.UpdraftHeight),
            Math.Min(end.UpdraftWidth, end.UpdraftHeight));
        var estimatedSpeed = Math.Max(EstimateTravelSpeed(start), EstimateTravelSpeed(end));
        // smoothprofile の速度成分を入力範囲で数値探索した結果から、1.5倍を刻み数の目安にします。
        // これは精度保証ではなく、通常域を安定側へ寄せながら極端値の計算量を抑えるための値です。
        var targetDistance = Math.Max(0.25, minimumDimension / 64);
        var required = (int)Math.Ceiling(estimatedSpeed / cachedFps / targetDistance);
        return Math.Clamp(required, MinimumUpdraftSubsteps, MaximumUpdraftSubsteps);
    }

    private static double EstimateTravelSpeed(RainfallParameters parameters)
    {
        var baseSpeed = parameters.Speed * (1 + parameters.SpeedVariation / 100);
        var windSpeed = parameters.WindEnabled
            ? parameters.WindSpeed * (1 + parameters.WindVariation / 100)
                * (1 + parameters.WindResponseVariation / 100)
            : 0;
        var localSpeed = parameters.SnowMotion == SnowMotionKind.Updraft
            ? parameters.UpdraftSpeed * 1.5
            : 0;
        var driftSpeed = parameters.DriftEnabled
            ? parameters.DriftWidth * parameters.DriftRate * (Math.Tau / 4 + 0.375)
            : 0;
        return baseSpeed + windSpeed + localSpeed + driftSpeed;
    }

    private WeatherVector WrapPosition(WeatherVector position)
    {
        if (diagonalOnset)
        {
            var local = ToDomainVector(position - domainCenter);
            return FromDomain(new(
                WeatherFields.Wrap(local.X + spanX / 2, spanX) - spanX / 2,
                WeatherFields.Wrap(local.Y + spanY / 2, spanY) - spanY / 2));
        }
        return new(
            cachedBounds.Left - margin + WeatherFields.Wrap(position.X - (cachedBounds.Left - margin), spanX),
            cachedBounds.Top - margin + WeatherFields.Wrap(position.Y - (cachedBounds.Top - margin), spanY));
    }

    private WeatherVector ToDomainVector(WeatherVector value)
        => new(domainCos * value.X - domainSin * value.Y, domainSin * value.X + domainCos * value.Y);

    private WeatherVector FromDomainVector(WeatherVector value)
        => new(domainCos * value.X + domainSin * value.Y, -domainSin * value.X + domainCos * value.Y);

    private WeatherVector FromDomain(WeatherVector value) => domainCenter + FromDomainVector(value);

    private static double WrappedDifference(double difference, double span)
    {
        difference %= span;
        if (difference > span / 2) difference -= span;
        if (difference < -span / 2) difference += span;
        return difference;
    }

    private static WeatherVector ParticleCommon(CommonTravel common, Particle particle)
        => common.Base + common.SpeedVariation * particle.SpeedRandom
            + common.Wind + common.WindResponseVariation * particle.WindRandom;

    private static WeatherVector CommonVelocity(
        RainfallParameters parameters, double seconds, double speedRandom, double windRandom)
        => ParticleCommon(CommonMotion.GetVelocity(parameters, seconds),
            new Particle(0, speedRandom, 0, 1, windRandom, 0, default, 0, 0, true));

    private static double BaseAngle(RainfallParameters parameters)
        => parameters.Kind == WeatherKind.Snow ? 0 : parameters.Angle;

    private void StoreLocalCheckpoint(long frame, LocalOffsets value)
    {
        localCheckpoints[frame] = value.Clone();
        var bytesPerCheckpoint = Math.Max(1L, particles.Length * (sizeof(double) * 2L + sizeof(bool)));
        var maximumCount = (int)Math.Clamp(MaximumLocalCacheBytes / bytesPerCheckpoint, 2, 256);
        while (localCheckpoints.Count > maximumCount)
        {
            var key = localCheckpoints.Keys.First(key => key != 0);
            localCheckpoints.Remove(key);
        }
    }

    private readonly record struct Particle(
        int Index,
        double SpeedRandom,
        double SizeRandom,
        double LengthScale,
        double WindRandom,
        double BirthSeconds,
        CommonTravel BirthCommon,
        double OriginX,
        double OriginY,
        bool StartsOutside);

    /// <summary>局所変位と画面外を通過した履歴をまとめて保存し、逆シークでも放出状態を復元します。</summary>
    private sealed class LocalOffsets(double[] x, double[] y, bool[] released)
    {
        public double[] X { get; } = x;
        public double[] Y { get; } = y;
        public bool[] Released { get; } = released;
        public int PendingReleaseCount { get; private set; } = released.Count(value => !value);
        public void Release(int index)
        {
            if (Released[index]) return;
            Released[index] = true;
            PendingReleaseCount--;
        }
        public LocalOffsets Clone() => new((double[])X.Clone(), (double[])Y.Clone(), (bool[])Released.Clone());
    }

    private readonly record struct CommonTravel(
        WeatherVector Base,
        WeatherVector SpeedVariation,
        WeatherVector Wind,
        WeatherVector WindResponseVariation)
    {
        public static CommonTravel operator +(CommonTravel left, CommonTravel right)
            => new(left.Base + right.Base, left.SpeedVariation + right.SpeedVariation,
                left.Wind + right.Wind, left.WindResponseVariation + right.WindResponseVariation);

        public static CommonTravel operator -(CommonTravel left, CommonTravel right)
            => new(left.Base - right.Base, left.SpeedVariation - right.SpeedVariation,
                left.Wind - right.Wind, left.WindResponseVariation - right.WindResponseVariation);

        public static CommonTravel operator *(CommonTravel value, double scale)
            => new(value.Base * scale, value.SpeedVariation * scale,
                value.Wind * scale, value.WindResponseVariation * scale);
    }

    /// <summary>共通移動を台形則で積分し、長尺でも粒数に比例しないキャッシュを保持します。</summary>
    private sealed class CommonMotion
    {
        private const int MaximumCheckpointCount = 16384;
        private readonly SortedDictionary<long, CommonTravel> checkpoints = new() { [0] = default };
        private readonly Dictionary<long, CommonTravel> samples = [];

        public void Reset()
        {
            checkpoints.Clear();
            checkpoints[0] = default;
            samples.Clear();
        }

        public CommonTravel Evaluate(long frame, int fps, Func<long, RainfallParameters> parametersAt)
        {
            if (frame <= 0 || fps <= 0) return default;
            if (samples.TryGetValue(frame, out var cached)) return cached;
            var startFrame = 0L;
            var value = default(CommonTravel);
            foreach (var pair in checkpoints)
            {
                if (pair.Key > frame) break;
                startFrame = pair.Key;
                value = pair.Value;
            }
            while (frame - startFrame >= CheckpointFrames)
            {
                value = Integrate(value, startFrame, startFrame + CheckpointFrames, fps, parametersAt);
                startFrame += CheckpointFrames;
                checkpoints[startFrame] = value;
                while (checkpoints.Count > MaximumCheckpointCount)
                {
                    var key = checkpoints.Keys.First(key => key != 0);
                    checkpoints.Remove(key);
                }
            }
            var result = Integrate(value, startFrame, frame, fps, parametersAt);
            if (samples.Count >= 16384) samples.Clear();
            samples[frame] = result;
            return result;
        }

        public CommonTravel EvaluateSeconds(
            double seconds, int fps, Func<long, RainfallParameters> parametersAt)
        {
            if (!double.IsFinite(seconds) || seconds <= 0 || fps <= 0) return default;
            var position = seconds * fps;
            var frame = checked((long)Math.Floor(position));
            var fraction = position - frame;
            var start = Evaluate(frame, fps, parametersAt);
            if (fraction == 0) return start;
            var first = GetVelocity(parametersAt(frame).Normalize(), frame / (double)fps);
            var last = GetVelocity(parametersAt(frame + 1).Normalize(), (frame + 1) / (double)fps);
            return start + first * ((fraction - fraction * fraction / 2) / fps)
                + last * (fraction * fraction / 2 / fps);
        }

        public static CommonTravel GetVelocity(RainfallParameters parameters, double seconds)
        {
            parameters = parameters.Normalize();
            var angle = BaseAngle(parameters);
            var baseVelocity = WeatherFields.Direction(parameters.Speed, angle);
            var wind = WeatherFields.WindVelocity(parameters, seconds);
            var sizeVariation = parameters.RainSizeMotionLinked
                ? parameters.ThicknessVariation / 100 : 1;
            var windSizeVariation = parameters.Kind == WeatherKind.Rain && parameters.RainSizeMotionLinked
                ? parameters.ThicknessVariation / 100 : 1;
            return new(
                baseVelocity,
                baseVelocity * (parameters.SpeedVariation / 100 * sizeVariation),
                wind,
                wind * (parameters.WindResponseVariation / 100 * windSizeVariation));
        }

        private static CommonTravel Integrate(
            CommonTravel value,
            long from,
            long to,
            int fps,
            Func<long, RainfallParameters> parametersAt)
        {
            var previous = GetVelocity(parametersAt(from).Normalize(), from / (double)fps);
            for (var frame = from; frame < to; frame++)
            {
                var next = GetVelocity(parametersAt(frame + 1).Normalize(), (frame + 1) / (double)fps);
                value += (previous + next) * (0.5 / fps);
                previous = next;
            }
            return value;
        }
    }
}
