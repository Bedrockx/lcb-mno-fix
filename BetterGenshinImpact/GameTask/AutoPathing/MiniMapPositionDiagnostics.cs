#nullable enable
using System;
using System.IO;
using System.Threading;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using Microsoft.Extensions.Logging;
using OpenCvSharp;

namespace BetterGenshinImpact.GameTask.AutoPathing;

/// <summary>
/// 小地图坐标识别诊断埋点（临时调试用）。
///
/// 目的：定位"回点时 Navigation.GetPosition 返回 (0,0)（识别失败）"到底卡在哪个环节：
///   1) 截图裁剪：MimiMapRect 区域是否取对（尺寸 / 是否纯黑 / 是否被 UI 遮挡）
///   2) 预处理：mask 占比是否异常（遮挡会让小地图圆盘 mask 缩水）
///   3) 角度识别：朝向置信度是否过低
///   4) 模板/SIFT 匹配：匹配置信度数值，走粗匹配还是精匹配，是否达阈值
///   5) Navigation 兜底：局部失败 → 全局匹配是否触发、是否仍失败
///
/// 用法：默认按开关 <see cref="Enabled"/> 输出结构化日志（DEBUG 级，带 [小地图诊断] 前缀，便于 grep）。
/// 识别失败（(0,0)）时，按节流间隔把当时的小地图截图 dump 到 log/minimap_diag/ 供肉眼看遮挡。
///
/// 该类为纯临时诊断设施，问题定位后可整体删除，不影响生产逻辑。
/// </summary>
public static class MiniMapPositionDiagnostics
{
    /// <summary>
    /// 诊断总开关。默认 true（本次就是为了跑一天收集日志）。
    /// 若要彻底静默，改成 false 重新编译即可。
    /// </summary>
    public static bool Enabled = true;

    /// <summary>
    /// 是否在识别失败时 dump 小地图截图。默认 true。
    /// </summary>
    public static bool DumpFailedFrame = true;

    /// <summary>
    /// 失败帧 dump 的最小间隔（毫秒），避免一秒几十帧把磁盘写爆。
    /// </summary>
    public static int DumpThrottleMs = 2000;

    private static long _lastDumpTicks;
    private static int _dumpSeq;

    private static ILogger Logger => TaskControl.Logger;

    /// <summary>
    /// 记录一次 GetPosition / GetPositionStable 调用的完整诊断信息。
    /// </summary>
    /// <param name="entry">入口名（GetPosition / GetPositionStable）。</param>
    /// <param name="miniMap">已裁剪的小地图彩色 Mat（裁剪后、预处理前）。</param>
    /// <param name="prevX">调用时的锚点 X（_prevX）。</param>
    /// <param name="prevY">调用时的锚点 Y（_prevY）。</param>
    /// <param name="resultPos">最终返回坐标。</param>
    /// <param name="branch">走的分支说明（如 "局部匹配命中" / "局部失败→全局命中" / "局部+全局均失败"）。</param>
    /// <param name="consecutiveFail">当前连续失败计数（仅 GetPosition 有意义，其它传 -1）。</param>
    public static void LogPosition(
        string entry,
        Mat miniMap,
        float prevX, float prevY,
        Point2f resultPos,
        string branch,
        int consecutiveFail)
    {
        if (!Enabled)
        {
            return;
        }

        try
        {
            bool failed = resultPos is { X: 0, Y: 0 };
            var (meanBrightness, blackRatio) = ComputeBrightness(miniMap);

            Logger.LogDebug(
                "[小地图诊断] {Entry} 结果=({Rx:F1},{Ry:F1}) {FailTag} | 分支={Branch} | 锚点=({Px:F1},{Py:F1}) 连续失败={Fail} | 截图={W}x{H} 均亮度={Mean:F1} 近黑占比={Black:P0} | 区域={RectW}x{RectH}@({RectX},{RectY})",
                entry,
                resultPos.X, resultPos.Y,
                failed ? "【识别失败】" : "OK",
                branch,
                prevX, prevY,
                consecutiveFail,
                miniMap.Width, miniMap.Height,
                meanBrightness, blackRatio,
                MapAssets.Instance.MimiMapRect.Width, MapAssets.Instance.MimiMapRect.Height,
                MapAssets.Instance.MimiMapRect.X, MapAssets.Instance.MimiMapRect.Y);

            if (failed && DumpFailedFrame)
            {
                DumpFrame(entry, miniMap);
            }
        }
        catch (Exception ex)
        {
            // 诊断埋点绝不能影响主流程：吞掉任何异常，仅记一条 debug 日志（含 ex）。
            Logger.LogDebug(ex, "[小地图诊断] 记录诊断信息时异常（已忽略，不影响识别）");
        }
    }

    /// <summary>
    /// 计算小地图整体平均亮度与近黑像素占比，用来判断"截图是否纯黑 / 区域是否取错 / 是否被全屏遮挡"。
    /// </summary>
    private static (double meanBrightness, double blackRatio) ComputeBrightness(Mat miniMap)
    {
        if (miniMap.Empty())
        {
            return (0, 1);
        }

        using var grey = miniMap.Channels() > 1
            ? miniMap.CvtColor(ColorConversionCodes.BGR2GRAY)
            : miniMap.Clone();

        var mean = Cv2.Mean(grey).Val0;

        // 近黑像素（< 25）占比
        using var blackMask = new Mat();
        Cv2.Threshold(grey, blackMask, 25, 255, ThresholdTypes.BinaryInv);
        double blackCount = Cv2.CountNonZero(blackMask);
        double total = grey.Rows * grey.Cols;
        double blackRatio = total > 0 ? blackCount / total : 1;

        return (mean, blackRatio);
    }

    /// <summary>
    /// 把识别失败时的小地图截图 dump 到 log/minimap_diag/，节流避免刷爆磁盘。
    /// </summary>
    private static void DumpFrame(string entry, Mat miniMap)
    {
        var nowTicks = DateTime.UtcNow.Ticks;
        var last = Interlocked.Read(ref _lastDumpTicks);
        if (nowTicks - last < TimeSpan.FromMilliseconds(DumpThrottleMs).Ticks)
        {
            return;
        }
        Interlocked.Exchange(ref _lastDumpTicks, nowTicks);

        try
        {
            var dir = Global.Absolute(@"log\minimap_diag");
            Directory.CreateDirectory(dir);
            var seq = Interlocked.Increment(ref _dumpSeq);
            var file = Path.Combine(dir, $"{DateTime.Now:yyyyMMdd_HHmmss}_{entry}_{seq:D4}.png");
            Cv2.ImWrite(file, miniMap);
            Logger.LogDebug("[小地图诊断] 识别失败帧已保存：{File}", file);
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "[小地图诊断] 保存失败帧截图异常（已忽略）");
        }
    }
}
