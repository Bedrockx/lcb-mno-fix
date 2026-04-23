namespace BgiCoordinatorServer.Models;

/// <summary>房主锄地配置，同步给所有成员</summary>
public class RoomConfig
{
    public int SyncTimeoutSeconds { get; set; } = 60;
    public int MinPlayersToSync { get; set; } = 0;
    public double SyncPointMinDistance { get; set; } = 30;
    public int StartRouteIndex { get; set; } = 0;
    public bool UseFixedDebugRoutes { get; set; } = false;
    public string FixedDebugRoutePath { get; set; } = "";
    public bool DebugMode { get; set; } = false;
    public bool ReturnToFightPointAfterBattle { get; set; } = false;
    public int ReturnToFightPointStaySeconds { get; set; } = 5;
    public int KazuhaPlayerIndex { get; set; } = 0;
    public int PartyTimeoutSeconds { get; set; } = 300;
    public bool MultiWorldEnabled { get; set; } = false;
    public int MultiWorldCount { get; set; } = 2;
    public string SelectedBuiltinRoute { get; set; } = "";
}
