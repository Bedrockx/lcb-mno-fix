using BgiCoordinatorServer.Models;
using BgiCoordinatorServer.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace BgiCoordinatorServer.Tests;

public class RouteVerificationSyncTests
{
    private readonly RoomManager _roomManager;
    private readonly Mock<ILogger<RoomManager>> _loggerMock;

    public RouteVerificationSyncTests()
    {
        _loggerMock = new Mock<ILogger<RoomManager>>();
        _roomManager = new RoomManager(_loggerMock.Object);
    }

    [Fact]
    public void RecordRouteVerificationDone_WithOfflinePlayer_ShouldIgnoreOfflinePlayer()
    {
        // Arrange
        var roomCode = "TEST123";
        var onlineConnectionId = "online-conn";
        var offlineConnectionId = "offline-conn";
        
        _roomManager.CreateRoom(roomCode, onlineConnectionId);
        
        // Add online player
        _roomManager.AddPlayer(roomCode, new PlayerInfo 
        { 
            ConnectionId = onlineConnectionId,
            LastHeartbeat = DateTime.UtcNow
        });
        
        // Add offline player (old heartbeat)
        _roomManager.AddPlayer(roomCode, new PlayerInfo 
        { 
            ConnectionId = offlineConnectionId,
            LastHeartbeat = DateTime.UtcNow.AddMinutes(-5) // 5 minutes ago
        });

        // Act - only online player reports
        var result = _roomManager.RecordRouteVerificationDone(roomCode, onlineConnectionId);

        // Assert - should return true because offline player is ignored
        Assert.True(result);
    }

    [Fact]
    public void RecordRouteVerificationDone_WithAllOnlinePlayersReported_ShouldReturnTrue()
    {
        // Arrange
        var roomCode = "TEST123";
        var conn1 = "conn1";
        var conn2 = "conn2";
        
        _roomManager.CreateRoom(roomCode, conn1);
        
        // Add two online players
        _roomManager.AddPlayer(roomCode, new PlayerInfo 
        { 
            ConnectionId = conn1,
            LastHeartbeat = DateTime.UtcNow
        });
        _roomManager.AddPlayer(roomCode, new PlayerInfo 
        { 
            ConnectionId = conn2,
            LastHeartbeat = DateTime.UtcNow
        });

        // Act - both players report
        var result1 = _roomManager.RecordRouteVerificationDone(roomCode, conn1);
        var result2 = _roomManager.RecordRouteVerificationDone(roomCode, conn2);

        // Assert
        Assert.False(result1); // First report should not complete
        Assert.True(result2);  // Second report should complete
    }

    [Fact]
    public void GetRouteVerificationStatus_ShouldReturnCorrectCounts()
    {
        // Arrange
        var roomCode = "TEST123";
        var onlineConn = "online";
        var offlineConn = "offline";
        
        _roomManager.CreateRoom(roomCode, onlineConn);
        
        _roomManager.AddPlayer(roomCode, new PlayerInfo 
        { 
            ConnectionId = onlineConn,
            LastHeartbeat = DateTime.UtcNow
        });
        _roomManager.AddPlayer(roomCode, new PlayerInfo 
        { 
            ConnectionId = offlineConn,
            LastHeartbeat = DateTime.UtcNow.AddMinutes(-5)
        });

        // Act
        _roomManager.RecordRouteVerificationDone(roomCode, onlineConn);
        var (onlineCount, reportedCount) = _roomManager.GetRouteVerificationStatus(roomCode);

        // Assert
        Assert.Equal(1, onlineCount);  // Only one online player
        Assert.Equal(1, reportedCount); // One reported
    }
}