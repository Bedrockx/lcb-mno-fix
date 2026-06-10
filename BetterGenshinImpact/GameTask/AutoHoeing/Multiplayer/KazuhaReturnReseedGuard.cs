#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using OpenCvSharp;

namespace BetterGenshinImpact.GameTask.AutoHoeing.Multiplayer;

/// <summary>
/// 万叶回点"异常坐标重新播种 + 重识别 + 有限重试 + 仍异常放弃"的编排 helper。
///
/// 由 hoeing-kazuha-return-abnormal-coord-reseed-moveto-fix spec 引入，作为**两条回点路径
/// 共同调用的唯一编排入口**，结构性保证两条路径对称（防复发核心约束 bugfix §3.9）：
/// - 路径 A：AutoFightTask.KazuhaContinuousReturnLoopAsync（战斗中持续回点后台循环）
/// - 路径 B：PathExecutor 战后聚物回点分支
///
/// 决策判定复用既有纯函数 <see cref="KazuhaCollectPositionGuardDecisions.IsRecognizedPositionTrustworthy"/>
/// 与 <see cref="KazuhaCollectPositionGuardDecisions.ComputeSeedAnchor"/>（恒等返回战斗点，§3.4）。
/// 本 helper 因含 IO（截图 + 识别 + 重播种 + 延时）无法纯函数化，故用委托注入这些 IO，
/// 使其在 PBT 中可用 stub 替换、可测。
///
/// reseedAnchor 委托语义仅对应 Navigation.SetPrevPosition(战斗点)，绝不 Navigation.Reset()（§3.5）——
/// 该约束体现在调用方（路径 A/B）传入的委托实现。
/// </summary>
public static class KazuhaReturnReseedGuard
{
    /// <summary>
    /// 每次"重新播种 + 重识别"之间的间隔毫秒数。
    /// 让小地图重绘稳定 / 角色位置有机会变化，使重识别产生不同结果（同一帧重算无意义）。
    /// 用户指令定为 100ms：小地图帧间隔约 16~33ms，100ms 覆盖 3~6 帧足够重绘；
    /// 3 次重试总延时 ≤ 300ms，不显著拖慢回点。
    /// </summary>
    public const int ReseedReSampleDelayMs = 100;

    /// <summary>
    /// 核心编排：判定首次坐标是否可信；异常则重播种战斗点锚点 + 重识别，最多 maxRetry 次；
    /// 任一次落入阈值即返回可信坐标，全部仍异常则返回"放弃本轮移动"。
    /// </summary>
    /// <param name="initialPos">调用方已识别的首帧坐标（调用前已做 (0,0) 过滤）。</param>
    /// <param name="fightPointX">本段战斗点 X（种子锚点，小地图坐标系）。</param>
    /// <param name="fightPointY">本段战斗点 Y。</param>
    /// <param name="threshold">异常判定阈值（= AutoHoeingConfig.KazuhaReturnAbnormalCoordThreshold，默认 50）。</param>
    /// <param name="maxRetry">最大重试次数（= AutoHoeingConfig.KazuhaReturnReseedRetryCount，默认 3）。</param>
    /// <param name="reseedAnchor">重播种委托：Navigation.SetPrevPosition(战斗点)。绝不 Reset。</param>
    /// <param name="reSample">重识别委托：CaptureToRectArea + Navigation.GetPosition，返回新坐标。</param>
    /// <param name="delay">延时委托：Task.Delay(ReseedReSampleDelayMs, ct)。</param>
    /// <param name="log">日志委托。</param>
    /// <param name="ct">取消令牌。</param>
    public static async Task<ReseedGuardResult> EvaluateAndReseedAsync(
        Point2f initialPos,
        double fightPointX, double fightPointY,
        double threshold,
        int maxRetry,
        Action reseedAnchor,
        Func<Point2f> reSample,
        Func<CancellationToken, Task> delay,
        Action<string> log,
        CancellationToken ct)
    {
        var (seedX, seedY) = KazuhaCollectPositionGuardDecisions.ComputeSeedAnchor(fightPointX, fightPointY);

        // 首次：用调用方已识别的 initialPos 判定。
        // 正常坐标（距战斗点 ≤ 阈值）第一次即采纳，零重播种 / 零重识别 / 零延时（Preservation）。
        if (KazuhaCollectPositionGuardDecisions.IsRecognizedPositionTrustworthy(initialPos, seedX, seedY, threshold))
        {
            return ReseedGuardResult.Move(initialPos, 0);
        }

        // 命中异常：进入"重播种 + 重识别"重试循环。
        var pos = initialPos;
        for (int attempt = 1; attempt <= maxRetry; attempt++)
        {
            reseedAnchor();              // SetPrevPosition(战斗点)：把漂移的陈旧锚点拨回战斗点
            await delay(ct);             // 极短延时让小地图重绘稳定 / 角色位置变化
            pos = reSample();            // 重新截图 + GetPosition

            // 重识别失败（(0,0)）：本次跳过，但仍计入 attempt，避免死循环。
            if (pos is { X: 0, Y: 0 })
            {
                log($"[重识别] 第{attempt}次识别失败(0,0)，继续重试");
                continue;
            }

            if (KazuhaCollectPositionGuardDecisions.IsRecognizedPositionTrustworthy(pos, seedX, seedY, threshold))
            {
                log($"[重识别] 第{attempt}次重识别落入阈值，采纳坐标");
                return ReseedGuardResult.Move(pos, attempt);
            }
        }

        // N 次重播种 + 重识别仍异常 → 放弃本轮移动（绝不以异常坐标 MoveTo / MoveCloseTo）。
        log($"[重识别] 连续{maxRetry}次重播种+重识别仍异常，放弃本轮移动");
        return ReseedGuardResult.Abandon(maxRetry);
    }
}

/// <summary>
/// helper 返回：要么"采纳一个可信坐标继续移动"，要么"放弃本轮移动"。
/// </summary>
public readonly struct ReseedGuardResult
{
    /// <summary>true = 拿到可信坐标可继续移动；false = N 次重试仍异常，放弃本轮移动。</summary>
    public bool ShouldMove { get; }

    /// <summary>ShouldMove = true 时的可信坐标（已落入阈值内）。</summary>
    public Point2f TrustedPos { get; }

    /// <summary>实际重识别重试次数（含触发的重播种次数），用于日志 / PBT 断言 ≤ N。</summary>
    public int RetryUsed { get; }

    private ReseedGuardResult(bool shouldMove, Point2f pos, int retryUsed)
    {
        ShouldMove = shouldMove;
        TrustedPos = pos;
        RetryUsed = retryUsed;
    }

    /// <summary>采纳可信坐标，继续移动。</summary>
    public static ReseedGuardResult Move(Point2f pos, int retryUsed) => new(true, pos, retryUsed);

    /// <summary>放弃本轮移动。</summary>
    public static ReseedGuardResult Abandon(int retryUsed) => new(false, default, retryUsed);
}
