namespace BetterGenshinImpact.GameTask.AutoHoeing.Services;

/// <summary>
/// 空轮预跳过后是否需要关闭房主房间的纯决策。
/// multiplayer-host-empty-round-preskip-orphan-room-not-closed-fix。
///
/// 背景：首轮/轮换空轮预跳过路径未与正常轮换 LeaveCurrentWorldAsync 对齐，
/// 漏关房主自己建的房间，导致服务端残留幽灵房间干扰后续轮次同步点放行。
/// </summary>
public static class PreSkipRoomCloseDecisions
{
    /// <summary>
    /// 预跳过场景下是否应由本节点关闭房间。
    /// 仅房主（建了房）需要关；成员未建房，不关。
    /// </summary>
    public static bool ShouldHostCloseRoomOnPreSkip(bool amIHost) => amIHost;
}
