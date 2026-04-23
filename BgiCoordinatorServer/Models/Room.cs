namespace BgiCoordinatorServer.Models;

public class Room
{
    public string Code { get; set; } = "";
    public string HostConnectionId { get; set; } = "";
    public List<PlayerInfo> Players { get; set; } = [];
    public DateTime CreatedAt { get; set; }

    /// <summary>syncPointId → 已到达的 connectionId 集合</summary>
    public Dictionary<string, HashSet<string>> ArrivalSets { get; set; } = [];

    /// <summary>syncPointId → 已完成战斗的 connectionId 集合</summary>
    public Dictionary<string, HashSet<string>> FightDoneSets { get; set; } = [];

    /// <summary>万叶玩家序号（0=不指定）</summary>
    public int KazuhaPlayerIndex { get; set; } = 0;

    /// <summary>房间白名单</summary>
    public List<string> Whitelist { get; set; } = [];

    /// <summary>已完成路线验证的 connectionId 集合</summary>
    public HashSet<string> RouteVerificationDoneSet { get; set; } = [];

    /// <summary>已加入世界的 connectionId 集合</summary>
    public HashSet<string> WorldJoinedSet { get; set; } = [];

    /// <summary>房间期望人数</summary>
    public int ExpectedPlayerCount { get; set; } = 4;

    /// <summary>房主锄地配置</summary>
    public RoomConfig? HostConfig { get; set; }

    /// <summary>房主是否已进入等待状态</summary>
    public bool HostReady { get; set; } = false;

    /// <summary>房主筛选后的最终路线文件名列表（按执行顺序）</summary>
    public List<string> HostRouteList { get; set; } = [];
}
