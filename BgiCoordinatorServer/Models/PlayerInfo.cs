namespace BgiCoordinatorServer.Models;

public class PlayerInfo
{
    public string ConnectionId { get; set; } = "";
    public string PlayerId { get; set; } = "";
    public string PlayerName { get; set; } = "";
    public string PlayerUid { get; set; } = "";
    public PlayerStatus Status { get; set; } = PlayerStatus.Waiting;
    public DateTime LastHeartbeat { get; set; }
}
