namespace BetterGenshinImpact.GameTask.AutoHoeing.Services;

/// <summary>
/// AnomalyDetector 节流决策（纯函数，PBT 友好）。
/// 把"复苏路径命中后是否应该 continue 跳过循环末尾节流"的决策从 RunDetectionLoop 中抽出，
/// 让 PBT 可以直接对决策撒输入跑性质，而不必触发 OpenCV / SignalR / 截图依赖。
///
/// 设计原则（与 .kiro/specs/anomaly-detector-revival-tight-loop-during-suppression
/// /design.md §Fix.3 对齐）：
/// - 仅依赖布尔输入，无外部状态、无 logger、无 client；
/// - 生产代码与 PBT 共享同一函数，避免双套实现漂移。
/// </summary>
public static class AnomalyDetectorThrottleDecisions
{
    /// <summary>
    /// 给定一次循环 tick 中"复苏路径是否命中 + 是否抑制点击"的状态，决定 RunDetectionLoop
    /// 是否应该在该分支末尾 continue。
    /// </summary>
    /// <param name="revivalHit">本 tick 是否进入了复苏分支（色块或模板任一命中）。</param>
    /// <param name="suppressAutoRevivalClick">TpTask.SuppressAutoRevivalClick 当前值。</param>
    /// <returns>
    /// true：应当 continue（即非抑制路径，原有"立即点击 + 800ms Delay 后跳过本轮其他检测"语义保留）；
    /// false：不 continue（包括"未命中"和"抑制 + 命中"两种情况，让循环走到末尾节流）。
    /// </returns>
    public static bool ShouldContinueAfterRevivalHit(
        bool revivalHit, bool suppressAutoRevivalClick)
    {
        if (!revivalHit) return false;          // 未命中：根本不进入分支，无需 continue
        return !suppressAutoRevivalClick;       // 仅非抑制 + 命中才 continue
    }
}
