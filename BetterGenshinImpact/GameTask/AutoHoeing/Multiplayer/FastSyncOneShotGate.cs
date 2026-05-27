using System.Threading;

namespace BetterGenshinImpact.GameTask.AutoHoeing.Multiplayer;

/// <summary>
/// 快速同步点抢报的"一次性 OR 门"。
///
/// 用 int + Interlocked.CompareExchange 实现：第一个 TryArm() 返回 true，
/// 此后任意线程 / 任意次数调用都返回 false（idempotent）。
///
/// 用途：抢报 watcher 路径与严格"距离 ≤ 2.0"路径并行，谁先抢到 gate 谁负责
/// 实际调用 _client.WaitForAllPlayersAsync；另一路径 silent return 不重复发起
/// SignalR 调用。详见 .kiro/specs/multiplayer-fast-sync-host-controlled/design.md §3.2
///
/// per-waypoint 迭代作用域：每次 waypoint 迭代构造一个新实例，迭代结束随
/// 局部变量被 GC 回收（FR18，禁止用静态字段持有）。
///
/// Validates: requirements FR15 / FR16 / FR17 / FR18
/// </summary>
internal sealed class FastSyncOneShotGate
{
    private int _gate;

    /// <summary>
    /// 尝试抢门。第一个调用线程返回 true（此后 _gate=1），其他全部返回 false。
    /// 多线程安全：使用 Interlocked.CompareExchange 原子 CAS。
    /// </summary>
    public bool TryArm()
    {
        return Interlocked.CompareExchange(ref _gate, 1, 0) == 0;
    }

    /// <summary>
    /// 当前 gate 是否仍可被抢（未被抢到）。仅用于 FastSyncDecisions.ShouldFastReport
    /// 的 gateAlreadyArmed 入参，避免 watcher 在 gate 已被抢后还做无意义的距离计算 +
    /// 截图调用。注意：本方法是"快速预读"，不保证原子语义；真正的去重由 TryArm 兜底。
    /// </summary>
    public bool IsArmable => Volatile.Read(ref _gate) == 0;
}
