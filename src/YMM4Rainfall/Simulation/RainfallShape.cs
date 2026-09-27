// SPDX-License-Identifier: MPL-2.0
using System.ComponentModel.DataAnnotations;

namespace YMM4Rainfall.Simulation;

internal enum RainfallShape
{
    [Display(Name = "線")]
    Streak = 0,
    [Display(Name = "丸")]
    Circle = 1,
    [Display(Name = "水滴")]
    CartoonDrop = 2,
    [Display(Name = "PNG画像")]
    Png = 3,
    [Display(Name = "水泡A")]
    Bubble = 4,
    [Display(Name = "水泡B")]
    LensBubble = 5,
    [Display(Name = "玉雪")]
    SnowRound = 7,
    [Display(Name = "ぼたん雪")]
    SnowClump = 8,
    [Display(Name = "雪の結晶")]
    SnowCrystal = 9,
}

internal enum WeatherKind
{
    [Display(Name = "雨")] Rain = 0,
    [Display(Name = "雪")] Snow = 1,
    [Display(Name = "泡")] Bubble = 2,
    [Display(Name = "カスタムPNG")] CustomPng = 3,
}

internal enum MotionBlurMode
{
    [Display(Name = "前後")] Symmetric = 0,
    [Display(Name = "後方")] Trailing = 1,
}

internal enum SnowMotionKind
{
    [Display(Name = "基本降雪")] Basic = 0,
    [Display(Name = "渦")] Vortex = 1,
    [Display(Name = "巻き上がり")] Updraft = 2,
}

internal enum SnowCrystalStyle
{
    [Display(Name = "混在")] Mixed = 0,
    [Display(Name = "6本枝")] SimpleBranches = 1,
    [Display(Name = "樹枝状")] Dendrite = 2,
    [Display(Name = "細かな樹枝状")] Fernlike = 3,
    [Display(Name = "幅広い星形")] StellarPlate = 4,
    [Display(Name = "扇形")] SectorPlate = 5,
    [Display(Name = "六角板")] HexagonalPlate = 6,
}

internal static class RainfallShapeVariants
{
    public static int Count(RainfallShape shape) => shape == RainfallShape.SnowCrystal
        ? (int)SnowCrystalStyle.HexagonalPlate : 4;

    public static int Select(RainfallParameters parameters, double unitRandom)
    {
        if (parameters.Shape == RainfallShape.SnowCrystal &&
            parameters.CrystalStyle is > SnowCrystalStyle.Mixed and <= SnowCrystalStyle.HexagonalPlate)
            return (int)parameters.CrystalStyle - 1;
        var count = Count(parameters.Shape);
        return Math.Clamp((int)(unitRandom * count), 0, count - 1);
    }
}
