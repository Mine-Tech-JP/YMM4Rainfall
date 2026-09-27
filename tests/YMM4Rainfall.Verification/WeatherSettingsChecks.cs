// SPDX-License-Identifier: MPL-2.0
using System.Text.Json.Nodes;
using YMM4Rainfall.Simulation;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.UndoRedo;
using YmmJson = YukkuriMovieMaker.Json.Json;

namespace YMM4Rainfall.Verification;

internal static class WeatherSettingsChecks
{
    public static void Run(Action<string, Action> check, Action<bool> assert)
    {
        void require(bool condition, string message)
        {
            try { assert(condition); }
            catch (Exception exception) { throw new InvalidOperationException(message, exception); }
        }

        void requireSavedAnimation(Animation expected, Animation actual, string bankName)
        {
            var expectedJson = JsonNode.Parse(YmmJson.GetJsonText(expected));
            var actualJson = JsonNode.Parse(YmmJson.GetJsonText(actual));
            require(JsonNode.DeepEquals(expectedJson, actualJson),
                $"{bankName} bankの保存対象Animationが一致しません。保存前={expectedJson} 保存後={actualJson}");
        }

        check("種類別設定: 切替後も値とAnimation途中点を保持", () =>
        {
            var effect = new RainfallEffect(useNewItemDefaults: false);
            var banks = new Dictionary<WeatherKind, RainfallSettings>
            {
                [WeatherKind.Rain] = effect.RainSettings,
                [WeatherKind.Snow] = effect.SnowSettings,
                [WeatherKind.Bubble] = effect.BubbleSettings,
                [WeatherKind.CustomPng] = effect.PngSettings,
            };
            var expected = new Dictionary<WeatherKind, (double From, double To, RainfallShape Shape)>();

            var index = 0;
            foreach (var (kind, bank) in banks)
            {
                effect.Kind = kind;
                var from = 11 + index;
                var to = 61 + index;
                FeatureChecks.SetLinear(effect.AmountAnimation, from, to);
                effect.SpeedAnimation.SetFirstValue(200 + index);
                effect.Opacity.SetFirstValue(70 + index);
                effect.Red.SetFirstValue(80 + index);
                expected[kind] = (from, to, bank.Shape);
                index++;
            }

            foreach (var kind in new[] { WeatherKind.Bubble, WeatherKind.Rain, WeatherKind.CustomPng, WeatherKind.Snow, WeatherKind.Rain })
            {
                effect.Kind = kind;
                var bank = banks[kind];
                var values = expected[kind];
                require(ReferenceEquals(effect.AmountAnimation, bank.AmountAnimation), $"{kind} の量Animationが対応bankを参照していません。");
                require(ReferenceEquals(effect.SpeedAnimation, bank.SpeedAnimation), $"{kind} の速度Animationが対応bankを参照していません。");
                require(effect.AmountAnimation.GetValue(50, 100, 30) == (values.From + values.To) / 2, $"{kind} のAnimation途中点が切替後に保持されません。");
                require(effect.SpeedAnimation.GetFirstValue() == 200 + (int)kind, $"{kind} の速度が切替後に保持されません。");
                require(effect.Opacity.GetFirstValue() == 70 + (int)kind, $"{kind} の不透明度が切替後に保持されません。");
                require(effect.Red.GetFirstValue() == 80 + (int)kind, $"{kind} の色が切替後に保持されません。");
                require(effect.Shape == values.Shape, $"{kind} の形状が切替後に保持されません。");
            }
        });

        check("PNG設定: 降雪用とカスタムPNGの選択を独立保持", () =>
        {
            var effect = new RainfallEffect(useNewItemDefaults: false);
            var snowId = Guid.NewGuid();
            var customId = Guid.NewGuid();

            effect.Kind = WeatherKind.Snow;
            effect.SnowAppearance = SnowAppearance.Png;
            effect.PngImageId = snowId;
            effect.PngImageRevision = 7;

            effect.Kind = WeatherKind.CustomPng;
            effect.PngImageId = customId;
            effect.PngImageRevision = 13;

            effect.Kind = WeatherKind.Snow;
            require(effect.PngImageId == snowId && effect.PngImageRevision == 7, "降雪用PNG参照がカスタムPNGの選択で変化しました。");
            require(effect.Shape == RainfallShape.Png, "降雪用PNGの形状が保持されません。");
            effect.Kind = WeatherKind.CustomPng;
            require(effect.PngImageId == customId && effect.PngImageRevision == 13, "カスタムPNG参照が降雪用PNGの選択で変化しました。");
        });

        check("PNG選択のUndo: IDと版を一度に復帰し他bankを維持", () =>
        {
            var effect = new RainfallEffect(useNewItemDefaults: false) { Kind = WeatherKind.Snow };
            effect.SnowAppearance = SnowAppearance.Png;
            var oldSnowId = Guid.NewGuid();
            var newSnowId = Guid.NewGuid();
            var customId = Guid.NewGuid();
            effect.PngSelection = $"{oldSnowId:N}|5";
            effect.PngSettings.PngReference = new(customId, 19);

            var events = new List<IUndoRedoCommandBase>();
            EventHandler<UndoRedoEventArgs> handler = (_, args) => events.Add(args.Command);
            effect.UndoRedoCommandCreated += handler;
            effect.PngSelection = $"{newSnowId:N}|11";
            effect.UndoRedoCommandCreated -= handler;

            require(events.Count == 1, $"PNG選択が{events.Count}個のUndoコマンドになりました。期待値は1個です。");
            require(effect.PngImageId == newSnowId && effect.PngImageRevision == 11, "PNG選択でIDと版を同時更新できません。");
            require(effect.PngSettings.PngImageId == customId && effect.PngSettings.PngImageRevision == 19, "雪用PNGの選択でカスタムPNG bankが変化しました。");
            require(events[0] is IUndoRedoCommand, "PNG選択のUndoコマンドを同期実行できません。");

            var command = (IUndoRedoCommand)events[0];
            effect.SetPngStatus("変更後PNG: 正常");
            command.Undo();
            require(effect.PngImageId == oldSnowId && effect.PngImageRevision == 5, "PNG選択のUndoでIDと版を同時復帰できません。");
            require(effect.PngStatus == "次の描画でPNG画像を確認します。", "Undo後に別のPNGの状態が残っています。");
            require(effect.PngSettings.PngImageId == customId && effect.PngSettings.PngImageRevision == 19, "PNG選択のUndoで他bankが変化しました。");
            command.Redo();
            require(effect.PngImageId == newSnowId && effect.PngImageRevision == 11, "PNG選択のRedoでIDと版を同時復帰できません。");
            require(effect.PngSettings.PngImageId == customId && effect.PngSettings.PngImageRevision == 19, "PNG選択のRedoで他bankが変化しました。");
        });

        check("PNG状態: 2入口で独立保持し参照変更時に確認待ちへ戻す", () =>
        {
            var effect = new RainfallEffect(useNewItemDefaults: false) { Kind = WeatherKind.Snow };
            effect.SnowAppearance = SnowAppearance.Png;
            effect.SetPngStatus("雪PNG: 正常");
            effect.Kind = WeatherKind.CustomPng;
            effect.SetPngStatus("カスタムPNG: 欠落");

            effect.Kind = WeatherKind.Snow;
            require(effect.PngStatus == "雪PNG: 正常", "雪用PNGの状態を種類切替後に保持できません。");
            effect.Kind = WeatherKind.CustomPng;
            require(effect.PngStatus == "カスタムPNG: 欠落", "カスタムPNGの状態を種類切替後に保持できません。");

            var replacementId = Guid.NewGuid();
            effect.PngSelection = $"{replacementId:N}|29";
            require(effect.PngStatus == "次の描画でPNG画像を確認します。", "PNG参照変更後に状態が次回描画の確認待ちへ戻りません。");
            effect.Kind = WeatherKind.Snow;
            require(effect.PngStatus == "雪PNG: 正常", "カスタムPNGの参照変更で雪用PNGの状態が変化しました。");
        });

        check("新保存形式: 全bankと非選択Animationを保存再読込", () =>
        {
            var effect = new RainfallEffect(useNewItemDefaults: false) { Kind = WeatherKind.Bubble };
            var snowId = Guid.NewGuid();
            var customId = Guid.NewGuid();
            FeatureChecks.SetLinear(effect.RainSettings.AmountAnimation, 10, 40);
            effect.SnowAppearances.SelectedShape = RainfallShape.Png;
            FeatureChecks.SetLinear(effect.SnowSettings.AmountAnimation, 20, 50);
            FeatureChecks.SetLinear(effect.BubbleSettings.AmountAnimation, 30, 60);
            FeatureChecks.SetLinear(effect.PngSettings.AmountAnimation, 40, 70);
            effect.SetAnimationParameters(100, 30);
            var sourceKeys = new KeyFrames();
            effect.SetKeyFrames(sourceKeys);
            sourceKeys.Insert(50);
            effect.RainSettings.AmountAnimation.Values[1].Value = 25;
            effect.SnowSettings.AmountAnimation.Values[1].Value = 35;
            effect.BubbleSettings.AmountAnimation.Values[1].Value = 45;
            effect.PngSettings.AmountAnimation.Values[1].Value = 55;
            effect.RainSettings.WindEnabled = true;
            effect.SnowAppearances.SelectedShape = RainfallShape.Png;
            effect.SnowSettings.SnowMotion = SnowMotionKind.Vortex;
            effect.SnowSettings.DriftEnabled = true;
            effect.SnowSettings.PngImageId = snowId;
            effect.SnowSettings.PngImageRevision = 17;
            effect.BubbleSettings.OutlineOpacity.SetFirstValue(42);
            effect.PngSettings.PngImageId = customId;
            effect.PngSettings.PngImageRevision = 23;

            var json = YmmJson.GetJsonText(effect);
            var root = JsonNode.Parse(json)!.AsObject();
            require(root[nameof(RainfallEffect.SchemaVersion)]?.GetValue<int>() == 2, "保存形式の版が2として保存されません。");
            require(root.ContainsKey(nameof(RainfallEffect.RainAppearances)) && root.ContainsKey(nameof(RainfallEffect.SnowAppearances)) &&
                root.ContainsKey(nameof(RainfallEffect.BubbleAppearances)) && root.ContainsKey(nameof(RainfallEffect.PngAppearances)), "4つのbankがトップレベルへ保存されません。");
            require(!root.ContainsKey(nameof(RainfallEffect.AmountAnimation)) && !root.ContainsKey(nameof(RainfallEffect.Shape)) &&
                !root.ContainsKey(nameof(RainfallEffect.PngImageId)), "UI proxyがトップレベルの保存値として重複しました。");

            var restored = YmmJson.LoadFromText<RainfallEffect>(json)!;
            require(restored.SchemaVersion == 2 && restored.IsSupported, "新保存形式を対応形式として再読込できません。");
            require(restored.Kind == WeatherKind.Bubble, "選択中の種類を再読込できません。");
            require(effect.RainSettings.AmountAnimation.Values.Count == 3 && restored.RainSettings.AmountAnimation.Values.Count == 3,
                "非選択の降雨bankにあるAnimation途中値を再読込できません。");
            requireSavedAnimation(effect.RainSettings.AmountAnimation, restored.RainSettings.AmountAnimation, "降雨");
            requireSavedAnimation(effect.SnowSettings.AmountAnimation, restored.SnowSettings.AmountAnimation, "降雪");
            requireSavedAnimation(effect.BubbleSettings.AmountAnimation, restored.BubbleSettings.AmountAnimation, "水泡");
            requireSavedAnimation(effect.PngSettings.AmountAnimation, restored.PngSettings.AmountAnimation, "PNG");
            require(restored.RainSettings.WindEnabled, "非選択の降雨bankにあるboolを再読込できません。");
            require(restored.SnowSettings is { Shape: RainfallShape.Png, SnowMotion: SnowMotionKind.Vortex, DriftEnabled: true }, "非選択の降雪bankにある固定値を再読込できません。");
            require(restored.SnowSettings.PngImageId == snowId && restored.SnowSettings.PngImageRevision == 17, "降雪用PNG参照を再読込できません。");
            require(restored.BubbleSettings.OutlineOpacity.GetFirstValue() == 42, "非選択の水泡bankにある輪郭を再読込できません。");
            require(restored.PngSettings.PngImageId == customId && restored.PngSettings.PngImageRevision == 23, "カスタムPNG参照を再読込できません。");

            restored.SetAnimationParameters(100, 30);
            var restoredKeys = new KeyFrames();
            restoredKeys.Insert(50);
            restored.SetKeyFrames(restoredKeys);
            var animationPairs = new[]
            {
                (Name: "降雨", Before: effect.RainSettings.AmountAnimation, After: restored.RainSettings.AmountAnimation),
                (Name: "降雪", Before: effect.SnowSettings.AmountAnimation, After: restored.SnowSettings.AmountAnimation),
                (Name: "水泡", Before: effect.BubbleSettings.AmountAnimation, After: restored.BubbleSettings.AmountAnimation),
                (Name: "PNG", Before: effect.PngSettings.AmountAnimation, After: restored.PngSettings.AmountAnimation),
            };
            foreach (var pair in animationPairs)
                foreach (var frame in new long[] { 0, 25, 50, 75, 100 })
                    require(pair.Before.GetValue(frame, 100, 30) == pair.After.GetValue(frame, 100, 30),
                        $"{pair.Name} bankのAnimation軌道が再読込後の{frame}フレームで一致しません。");
        });

        check("保存形式の判定: 正常値だけ対応し未知値を維持", () =>
        {
            var current = new RainfallEffect(useNewItemDefaults: false) { Kind = WeatherKind.Snow };
            current.SnowAppearances.SelectedShape = RainfallShape.SnowCrystal;
            require(current.IsSupported, "新規作成した正常な保存形式が未対応扱いです。");
            var normal = YmmJson.LoadFromText<RainfallEffect>(YmmJson.GetJsonText(current))!;
            require(normal.IsSupported && normal.SchemaVersion == 2 && normal.Kind == WeatherKind.Snow && normal.Shape == RainfallShape.SnowCrystal,
                "正常な新保存形式をYMM JSONで対応状態のまま再読込できません。");

            var unknownSchema = new RainfallEffect(useNewItemDefaults: false) { SchemaVersion = 99, Kind = WeatherKind.Bubble };
            unknownSchema.BubbleAppearances.SelectedShape = RainfallShape.LensBubble;
            var restoredSchema = YmmJson.LoadFromText<RainfallEffect>(YmmJson.GetJsonText(unknownSchema))!;
            require(!restoredSchema.IsSupported && restoredSchema.SchemaVersion == 99 && restoredSchema.Kind == WeatherKind.Bubble && restoredSchema.Shape == RainfallShape.LensBubble,
                "未知の保存版を値を維持した未対応状態として再読込できません。");

            var unknownKind = new RainfallEffect(useNewItemDefaults: false) { Kind = (WeatherKind)99 };
            unknownKind.RainAppearances.SelectedShape = RainfallShape.CartoonDrop;
            var restoredKind = YmmJson.LoadFromText<RainfallEffect>(YmmJson.GetJsonText(unknownKind))!;
            require(!restoredKind.IsSupported && restoredKind.Kind == (WeatherKind)99 && restoredKind.RainSettings.Shape == RainfallShape.CartoonDrop,
                "未知の種類を降雨などへ変換せず未対応状態として再読込できません。");

            var mismatch = new RainfallEffect(useNewItemDefaults: false) { Kind = WeatherKind.Snow };
            mismatch.SnowAppearances.SelectedShape = RainfallShape.Bubble;
            var restoredMismatch = YmmJson.LoadFromText<RainfallEffect>(YmmJson.GetJsonText(mismatch))!;
            require(!restoredMismatch.IsSupported && restoredMismatch.Kind == WeatherKind.Snow && restoredMismatch.Shape == RainfallShape.Bubble,
                "種類と形の不一致を黙って補正せず未対応状態として再読込できません。");
        });

        check("子bankのUndo: boolとAnimationを親へ一度伝播", () =>
        {
            var effect = new RainfallEffect(useNewItemDefaults: false) { Kind = WeatherKind.Snow };
            var notices = new List<string?>();
            effect.PropertyChanged += (_, args) => notices.Add(args.PropertyName);

            var boolEvents = new List<IUndoRedoCommandBase>();
            EventHandler<UndoRedoEventArgs> boolHandler = (_, args) => boolEvents.Add(args.Command);
            effect.UndoRedoCommandCreated += boolHandler;
            effect.SnowSettings.WindEnabled = true;
            effect.UndoRedoCommandCreated -= boolHandler;
            require(boolEvents.Count == 1, $"子bankのbool変更が親へ{boolEvents.Count}回伝播しました。期待値は1回です。");
            require(boolEvents[0] is IUndoRedoCommand, "bool変更のUndoコマンドを同期実行できません。");
            var boolCommand = (IUndoRedoCommand)boolEvents[0];
            notices.Clear();
            boolCommand.Undo();
            require(!effect.SnowSettings.WindEnabled && !effect.WindEnabled, "bool変更をUndoできません。");
            require(notices.Contains(nameof(RainfallEffect.WindEnabled)), "bool変更のUndo後にUI proxy通知がありません。");
            notices.Clear();
            boolCommand.Redo();
            require(effect.SnowSettings.WindEnabled && effect.WindEnabled, "bool変更をRedoできません。");
            require(notices.Contains(nameof(RainfallEffect.WindEnabled)), "bool変更のRedo後にUI proxy通知がありません。");

            var animationEvents = new List<IUndoRedoCommandBase>();
            var animationNotices = new List<string?>();
            effect.AmountAnimation.PropertyChanged += (_, args) => animationNotices.Add(args.PropertyName);
            EventHandler<UndoRedoEventArgs> animationHandler = (_, args) => animationEvents.Add(args.Command);
            effect.UndoRedoCommandCreated += animationHandler;
            var original = effect.AmountAnimation.GetFirstValue();
            effect.SnowSettings.AmountAnimation.SetFirstValue(original + 9);
            effect.UndoRedoCommandCreated -= animationHandler;
            require(animationEvents.Count == 1, $"子bankのAnimation変更が親へ{animationEvents.Count}回伝播しました。期待値は1回です。");
            require(animationEvents[0] is IUndoRedoCommand, "Animation変更のUndoコマンドを同期実行できません。");
            var animationCommand = (IUndoRedoCommand)animationEvents[0];
            animationNotices.Clear();
            animationCommand.Undo();
            require(effect.AmountAnimation.GetFirstValue() == original, "Animation変更をUndoできません。");
            require(animationNotices.Count > 0, "Animation変更のUndo後に選択中AnimationからUI通知がありません。");
            animationNotices.Clear();
            animationCommand.Redo();
            require(effect.AmountAnimation.GetFirstValue() == original + 9, "Animation変更をRedoできません。");
            require(animationNotices.Count > 0, "Animation変更のRedo後に選択中AnimationからUI通知がありません。");
        });

        check("種類のUndo: 一度のコマンドでbank参照とUI通知を復帰", () =>
        {
            var effect = new RainfallEffect(useNewItemDefaults: false);
            var rainAmount = effect.RainSettings.AmountAnimation;
            var snowAmount = effect.SnowSettings.AmountAnimation;
            var notices = new List<string?>();
            effect.PropertyChanged += (_, args) => notices.Add(args.PropertyName);
            var events = new List<IUndoRedoCommandBase>();
            EventHandler<UndoRedoEventArgs> handler = (_, args) => events.Add(args.Command);
            effect.UndoRedoCommandCreated += handler;

            effect.Kind = WeatherKind.Snow;
            effect.UndoRedoCommandCreated -= handler;
            require(events.Count == 1, $"種類の切替が{events.Count}個のUndoコマンドになりました。期待値は1個です。");
            require(effect.Kind == WeatherKind.Snow && ReferenceEquals(effect.AmountAnimation, snowAmount), "種類の切替後に降雪bankを参照していません。");
            require(notices.Contains(nameof(RainfallEffect.Kind)) && notices.Contains(nameof(RainfallEffect.AmountAnimation)), "種類の切替後に必要なUI通知がありません。");
            require(events[0] is IUndoRedoCommand, "種類切替のUndoコマンドを同期実行できません。");

            var command = (IUndoRedoCommand)events[0];
            notices.Clear();
            command.Undo();
            require(effect.Kind == WeatherKind.Rain && ReferenceEquals(effect.AmountAnimation, rainAmount), "種類の切替をUndoして降雨bankへ戻せません。");
            require(notices.Contains(nameof(RainfallEffect.Kind)) && notices.Contains(nameof(RainfallEffect.AmountAnimation)), "種類切替のUndo後に必要なUI通知がありません。");
            notices.Clear();
            command.Redo();
            require(effect.Kind == WeatherKind.Snow && ReferenceEquals(effect.AmountAnimation, snowAmount), "種類の切替をRedoして降雪bankへ戻せません。");
            require(notices.Contains(nameof(RainfallEffect.Kind)) && notices.Contains(nameof(RainfallEffect.AmountAnimation)), "種類切替のRedo後に必要なUI通知がありません。");
        });
    }
}
