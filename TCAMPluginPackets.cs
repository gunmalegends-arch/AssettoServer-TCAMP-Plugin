using AssettoServer.Network.ClientMessages;
using AssettoServer.Shared.Model;

namespace TCAMPlugin.Packets;

[OnlineEvent(Key = "TCAM_GameState")]
public class GameStatePacket : OnlineEvent<GameStatePacket>
{
    [OnlineEventField(Name = "state")]
    public int State = 0;
    
    [OnlineEventField(Name = "currentRound")]
    public int CurrentRound = 0;
    
    [OnlineEventField(Name = "myRole")]
    public int MyRole = 0; // 0=Spectator, 1=Cat, 2=Mouse
    
    [OnlineEventField(Name = "catName", Size = 50)]
    public string CatName = "";
    
    [OnlineEventField(Name = "mouseName", Size = 50)]
    public string MouseName = "";
    
    [OnlineEventField(Name = "catWins")]
    public int CatWins = 0;
    
    [OnlineEventField(Name = "mouseWins")]
    public int MouseWins = 0;
}

[OnlineEvent(Key = "TCAM_RoundStart")]
public class RoundStartPacket : OnlineEvent<RoundStartPacket>
{
    [OnlineEventField(Name = "round")]
    public int Round = 0;
    
    [OnlineEventField(Name = "myRole")]
    public int MyRole = 0;
    
    [OnlineEventField(Name = "catName", Size = 50)]
    public string CatName = "";
    
    [OnlineEventField(Name = "mouseName", Size = 50)]
    public string MouseName = "";
}

[OnlineEvent(Key = "TCAM_RoundResult")]
public class RoundResultPacket : OnlineEvent<RoundResultPacket>
{
    [OnlineEventField(Name = "winner")]
    public int Winner = 0;
    
    [OnlineEventField(Name = "reason", Size = 100)]
    public string Reason = "";
    
    [OnlineEventField(Name = "round")]
    public int Round = 0;
    
    [OnlineEventField(Name = "didIWin")]
    public bool DidIWin = false;
    
    [OnlineEventField(Name = "winnerName", Size = 50)]
    public string WinnerName = "";
}

[OnlineEvent(Key = "TCAM_GameComplete")]
public class GameCompletePacket : OnlineEvent<GameCompletePacket>
{
    [OnlineEventField(Name = "winnerName", Size = 50)]
    public string WinnerName = "";
    
    [OnlineEventField(Name = "winCount")]
    public int WinCount = 0;
    
    [OnlineEventField(Name = "didIWin")]
    public bool DidIWin = false;
}

[OnlineEvent(Key = "TCAM_LapResult")]
public class LapResultPacket : OnlineEvent<LapResultPacket>
{
    [OnlineEventField(Name = "lapTimeMs")]
    public int LapTimeMs = 0;

    [OnlineEventField(Name = "lapTimeFormatted", Size = 20)]
    public string LapTimeFormatted = "";

    [OnlineEventField(Name = "cuts")]
    public int Cuts = 0;

    [OnlineEventField(Name = "collisions")]
    public int Collisions = 0;

    [OnlineEventField(Name = "saved")]
    public bool Saved = false;

    [OnlineEventField(Name = "reason", Size = 100)]
    public string Reason = "";
}