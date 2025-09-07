using AssettoServer.Commands;
using Qmmands;

namespace TCAMPlugin;

public class TCAMCommandModule : ACModuleBase
{
    private readonly TCAMService _tcamService;

    public TCAMCommandModule(TCAMService tcamService)
    {
        _tcamService = tcamService;
    }

    [Command("startgame")]
    public void StartGame()
    {
        if (Client == null)
        {
            Reply("Command can only be used by connected players.");
            return;
        }
        
        var result = _tcamService.TryStartGame(Client);
        Reply(result.Message);
    }

    [Command("stopgame")]
    public void StopGame()
    {
        if (Client == null)
        {
            Reply("Command can only be used by connected players.");
            return;
        }
        
        var result = _tcamService.TryStopGame(Client);
        Reply(result.Message);
    }

    [Command("tcamstatus")]
    public void Status()
    {
        var status = _tcamService.GetGameStatus();
        Reply(status);
    }

    [Command("tcamhelp")]
    public void Help()
    {
        Reply("Cat & Mouse Commands:");
        Reply("/startgame - Start a new game (requires 2+ players)");
        Reply("/stopgame - Stop current game");
        Reply("/tcamstatus - Show current game status");
        Reply("/tcamhelp - Show this help");
    }
}