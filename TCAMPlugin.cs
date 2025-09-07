using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using System.Numerics;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using AssettoServer.Network.Tcp;
using AssettoServer.Server;
using AssettoServer.Shared.Model;
using AssettoServer.Network.ClientMessages;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TCAMPlugin.Packets;
using Serilog;

namespace TCAMPlugin;

/* Todo List For The Future:
    Add penalties for ramming and such with collision detection. 
    Instead of restarting session and making people stay in race mode just change it to race mode if in another session type, moving back once rounds end.
    Ingame leaderboard support. A lua list showing 10 entries per page showcasing players mirroring the website.
    Improved elo system instead of just win/loss.
    For ehrmm a freeroam based server maybe something to make match making only happen for the clients that want to play cat and mouse.
*/

public enum GameState
{
    Waiting,
    InProgress,
    RoundComplete,
    GameComplete
}

public enum RoleType
{
    None,
    Cat,
    Mouse
}

public enum RoundResult
{
    CatWins,
    MouseWins,
    Draw
}

public class PlayerState
{
    public int ConnectionId { get; set; }
    public ulong Guid { get; set; }
    public string Name { get; set; } = "";
    public RoleType Role { get; set; } = RoleType.None;
    public int Wins { get; set; } = 0;
    public float SplinePosition { get; set; } = 0f;
    public bool HasFinishedLap { get; set; } = false;
    public int LapCount { get; set; } = 0;
}

public class GameResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
}

public class WebhookPayload
{
    public string PlayerName { get; set; } = "";
    public string SteamId { get; set; } = "";
    public int UserScore { get; set; }
    public int OpponentScore { get; set; }
    public string OpponentName { get; set; } = "";
    public string OpponentSteamId { get; set; } = "";
    public int RoundsPlayed { get; set; }
    public long GameEndTime { get; set; }
    public string Password { get; set; } = "";
}

public class TCAMService : BackgroundService
{
    private readonly EntryCarManager _entryCarManager;
    private readonly SessionManager _sessionManager;
    private readonly CSPServerScriptProvider _scriptProvider;   
    private readonly CSPClientMessageTypeManager _messageTypeManager;
    private readonly ILogger<TCAMService> _logger;
    private readonly TCAMPluginConfiguration _config;
    private readonly HttpClient _httpClient;

    private GameState _gameState = GameState.Waiting;
    private readonly Dictionary<int, PlayerState> _players = new();
    private int _currentRound = 0;
    private PlayerState? _currentCat;
    private PlayerState? _currentMouse;
    private bool _gameActive = false;
    private bool _hasRoundBeenCompleted = false;

    public TCAMService(
        EntryCarManager entryCarManager,
        SessionManager sessionManager,
        CSPServerScriptProvider scriptProvider,
        CSPClientMessageTypeManager messageTypeManager,
        ILogger<TCAMService> logger,
        TCAMPluginConfiguration config)
    {
        _entryCarManager = entryCarManager ?? throw new ArgumentNullException(nameof(entryCarManager));
        _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
        _scriptProvider = scriptProvider ?? throw new ArgumentNullException(nameof(scriptProvider));
        _messageTypeManager = messageTypeManager ?? throw new ArgumentNullException(nameof(messageTypeManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _config = config ?? throw new ArgumentNullException(nameof(config));

        // Initialize HttpClient with timeout
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(_config.WebhookTimeoutSeconds)
        };

        // Register custom packets with proper no-op handlers
        _messageTypeManager.RegisterOnlineEvent<GameStatePacket>((client, packet) => { /* no-op */ });
        _messageTypeManager.RegisterOnlineEvent<RoundResultPacket>((client, packet) => { /* no-op */ });
        _messageTypeManager.RegisterOnlineEvent<GameCompletePacket>((client, packet) => { /* no-op */ });
        _messageTypeManager.RegisterOnlineEvent<RoundStartPacket>((client, packet) => { /* no-op */ });
        _messageTypeManager.RegisterOnlineEvent<LapResultPacket>((client, packet) => { /* no-op */ });

        // Load Lua scripts
        LoadLuaScripts();

        // Hook events
        _entryCarManager.ClientConnected += OnClientConnected;
        _entryCarManager.ClientDisconnected += OnClientDisconnected;
    }



    public override void Dispose()
    {
        _httpClient?.Dispose();
        base.Dispose();
    }



    public GameResult TryStartGame(ACTcpClient client)
    {
        if (_gameActive)
        {
            return new GameResult { Success = false, Message = "Game is already active!" };
        }

        var playerCount = _entryCarManager.ConnectedCars.Count;
        if (playerCount < _config.MinPlayersToStart)
        {
            return new GameResult { Success = false, Message = $"Need at least {_config.MinPlayersToStart} players to start. Currently: {playerCount}" };
        }

        StartNewGame();
        BroadcastChatMessage($"Cat and Mouse game started by {client.Name}!");
        return new GameResult { Success = true, Message = "Game started successfully!" };
    }


    public GameResult TryStopGame(ACTcpClient client)
    {
        if (!_gameActive)
        {
            return new GameResult { Success = false, Message = "No game is currently active." };
        }

        EndGame();
        BroadcastChatMessage($"Cat and Mouse game stopped by {client.Name}.");
        return new GameResult { Success = true, Message = "Game stopped successfully!" };
    }


    public string GetGameStatus()
    {
        if (_gameActive)
        {
            var status = $"Game State: {_gameState}, Round: {_currentRound} (Best of 3)\n";
            status += $"Cat: {_currentCat?.Name ?? "None"}, Mouse: {_currentMouse?.Name ?? "None"}\n";
            status += $"Score - Cat: {_currentCat?.Wins ?? 0}, Mouse: {_currentMouse?.Wins ?? 0}";
            return status;
        }
        else
        {
            return "No game currently active. Use /startgame to begin.";
        }
    }


    private void LoadLuaScripts()
    {
        var scriptsToLoad = new[] { "catmouse.lua" };

        foreach (var scriptName in scriptsToLoad)
        {
            var luaPath = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!, "lua", scriptName);
            if (!File.Exists(luaPath))
            {
                Log.Warning("[TCAM] Lua script not found at {LuaPath}", luaPath);
                continue;
            }

            var luaScript = File.ReadAllText(luaPath);
            _scriptProvider.AddScript(luaScript, scriptName);
            Log.Information("[TCAM] Loaded script: {ScriptName}", scriptName);
        }
    }


    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Subscribe existing clients
        foreach (var car in _entryCarManager.ConnectedCars.Values)
        {
            Subscribe(car.Client);
        }

        // Keep the service running
        while (!stoppingToken.IsCancellationRequested)
        {
            if (_gameActive)
            {
                await MonitorGame();
            }
            await Task.Delay(100, stoppingToken);
        }
    }


    private async Task MonitorGame()
    {
        if (_gameState != GameState.InProgress) return;

        // Update spline positions
        foreach (var kvp in _entryCarManager.ConnectedCars)
        {
            var car = kvp.Value;
            if (car.Client != null && _players.TryGetValue(car.Client.SessionId, out var player))
            {
                player.SplinePosition = car.Status.NormalizedPosition;
            }
        }

        await Task.CompletedTask;
    }


    private void OnClientConnected(ACTcpClient client, EventArgs args) => Subscribe(client);
    
    private void OnClientDisconnected(ACTcpClient client, EventArgs args) 
    {
        Unsubscribe(client);
        
        // Check if a game participant disconnects
        if (_gameActive && (_currentCat?.ConnectionId == client.SessionId || _currentMouse?.ConnectionId == client.SessionId))
        {
            var disconnectedPlayer = _players.GetValueOrDefault(client.SessionId);
            var remainingPlayer = disconnectedPlayer?.ConnectionId == _currentCat?.ConnectionId ? _currentMouse : _currentCat;
            
            _logger.LogInformation("[TCAM] Game participant {DisconnectedPlayer} disconnected", disconnectedPlayer?.Name ?? "Unknown");
            
            // If a round has already been completed, give the win to the remaining player
            if (_hasRoundBeenCompleted && remainingPlayer != null)
            {
                _logger.LogInformation("[TCAM] Round already completed, awarding game win to remaining player {RemainingPlayer}", remainingPlayer.Name);
                
                // Set remaining player as winner with 2 wins (game winner)
                remainingPlayer.Wins = 2;
                
                // Handle game complete
                HandleGameComplete(remainingPlayer, "Player disconnection after round completion");
                BroadcastChatMessage($"Game won by {remainingPlayer.Name} due to opponent disconnection!");
            }
            else
            {
                // No round completed yet, just end the game
                EndGame();
                BroadcastChatMessage("Game ended due to player disconnection.");
            }
        }
        
        _players.Remove(client.SessionId);
    }


    private void Subscribe(ACTcpClient? client)
    {
        if (client == null) return;
        client.LapCompleted += OnLapCompleted;

        // Add player if not exists - using SessionId (Connection ID) as key
        if (!_players.ContainsKey(client.SessionId))
        {
            _players[client.SessionId] = new PlayerState
            {
                ConnectionId = client.SessionId,
                Guid = client.Guid,
                Name = client.Name ?? "Unknown"
            };

            _logger.LogInformation("[TCAM] Player subscribed: {Name} [ID:{ConnectionId}] [GUID:{Guid}]",
                client.Name, client.SessionId, client.Guid);
        }
    }


    private void Unsubscribe(ACTcpClient? client)
    {
        if (client == null) return;
        client.LapCompleted -= OnLapCompleted;
    }


    private void BroadcastChatMessage(string message)
    {
        _logger.LogInformation("[TCAM] Broadcasting chat message: {Message}", message);
        Console.WriteLine($"[TCAM] Chat: {message}");

        foreach (var car in _entryCarManager.ConnectedCars.Values)
        {
            car.Client?.SendChatMessage(message);
        }
    }


    private void StartNewGame()
    {
        _logger.LogInformation("[TCAM] Starting new Cat and Mouse game");

        _gameActive = true;
        _currentRound = 1;
        _gameState = GameState.InProgress;
        _hasRoundBeenCompleted = false;

        // Reset all players
        foreach (var player in _players.Values)
        {
            player.Wins = 0;
            player.HasFinishedLap = false;
            player.LapCount = 0;
        }

        AssignRoles();
        StartNewRound();
    }


    private void AssignRoles()
    {
        // Get active players using connection IDs
        var activePlayers = _players.Values
            .Where(p => _entryCarManager.ConnectedCars.Values.Any(c => c.Client?.SessionId == p.ConnectionId))
            .ToList();

        if (activePlayers.Count < 2)
        {
            _logger.LogWarning("[TCAM] Not enough players for Cat and Mouse. Active players: {Count}", activePlayers.Count);
            return;
        }

        // Assign roles based on connection order
        var sortedPlayers = activePlayers.OrderBy(p => p.ConnectionId).ToList();

        _currentCat = sortedPlayers[0];
        _currentMouse = sortedPlayers[1];

        _currentCat.Role = RoleType.Cat;
        _currentMouse.Role = RoleType.Mouse;

        // Set everyone else as spectators
        foreach (var player in sortedPlayers.Skip(2))
        {
            player.Role = RoleType.None;
        }

        _logger.LogInformation("[TCAM] Roles assigned - Cat: {Cat} [ID:{CatId}], Mouse: {Mouse} [ID:{MouseId}]",
            _currentCat.Name, _currentCat.ConnectionId, _currentMouse.Name, _currentMouse.ConnectionId);
    }


    private void StartNewRound()
    {
        _logger.LogInformation("[TCAM] Starting round {Round} (Best of 3 - Cat: {CatWins}, Mouse: {MouseWins})",
            _currentRound, _currentCat?.Wins ?? 0, _currentMouse?.Wins ?? 0);

        _hasRoundBeenCompleted = false;

        // Reset round state
        foreach (var player in _players.Values)
        {
            player.HasFinishedLap = false;
            player.LapCount = 0;
        }

        // Restart session
        if (!_sessionManager.RestartSession())
        {
            _logger.LogWarning("[TCAM] Failed to restart session");
        }

        // Send game state and round start with proper timing, then teleport players
        Task.Delay(2000).ContinueWith(_ =>
        {
            SendGameStateToAllPlayers();
            Task.Delay(800).ContinueWith(__ =>
            {
                SendRoundStartToAllPlayers();
                // Teleport players after configured delay
                if (_config.EnableTeleport)
                {
                    Task.Delay(_config.TeleportDelaySeconds * 1000).ContinueWith(___ =>
                    {
                        TeleportPlayersToStartPositions();
                    });
                }
            });
        });
    }


    private void TeleportPlayersToStartPositions()
    {
        if (_currentCat == null || _currentMouse == null)
        {
            _logger.LogWarning("[TCAM] Cannot teleport players - cat or mouse not assigned");
            return;
        }

        try
        {
            // Find and teleport the cat
            var catCar = _entryCarManager.ConnectedCars.Values
                .FirstOrDefault(c => c.Client?.SessionId == _currentCat.ConnectionId);

            if (catCar?.Client != null)
            {
                var catPosition = _config.GetCatStartVector3();
                var catDirection = _config.GetCatDirectionVector();

                _logger.LogInformation("[TCAM] Teleporting Cat {Name} to position {Position} with direction {Direction}",
                    catCar.Client.Name, catPosition, catDirection);

                catCar.Client.SendTeleportCarPacket(catPosition, catDirection);
            }
            else
            {
                _logger.LogWarning("[TCAM] Could not find client for cat player {Name}", _currentCat.Name);
            }

            // Find and teleport the mouse
            var mouseCar = _entryCarManager.ConnectedCars.Values
                .FirstOrDefault(c => c.Client?.SessionId == _currentMouse.ConnectionId);

            if (mouseCar?.Client != null)
            {
                var mousePosition = _config.GetMouseStartVector3();
                var mouseDirection = _config.GetMouseDirectionVector();

                _logger.LogInformation("[TCAM] Teleporting Mouse {Name} to position {Position} with direction {Direction}",
                    mouseCar.Client.Name, mousePosition, mouseDirection);

                mouseCar.Client.SendTeleportCarPacket(mousePosition, mouseDirection);
            }
            else
            {
                _logger.LogWarning("[TCAM] Could not find client for mouse player {Name}", _currentMouse.Name);
            }

            BroadcastChatMessage($"Players teleported! Cat: {_currentCat.Name} | Mouse: {_currentMouse.Name} | GO!");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TCAM] Error teleporting players to start positions");
        }
    }


    private void OnLapCompleted(ACTcpClient client, LapCompletedEventArgs args)
    {
        if (!_gameActive || _gameState != GameState.InProgress) return;
        if (args == null || client == null) return;

        if (!_players.TryGetValue(client.SessionId, out var player)) return;

        uint lapTimeMs = args.Packet.LapTime;
        string lapTimeFormatted = TimeSpan.FromMilliseconds(lapTimeMs).ToString(@"mm\:ss\.fff");
        int cuts = args.Packet.Cuts;

        // Increment lap count
        player.LapCount++;

        // Get current spline position when lap ends
        var car = _entryCarManager.ConnectedCars.Values.FirstOrDefault(c => c.Client?.SessionId == client.SessionId);
        var splinePosition = car?.Status.NormalizedPosition ?? 0f;
        player.SplinePosition = splinePosition;

        _logger.LogInformation("[TCAM] Lap completed: {Name} ({Role}) [ID:{ConnectionId}] - Lap {LapCount} - {LapTime} - Cuts: {Cuts} - Position: {SplinePosition:F4}",
            player.Name, player.Role, player.ConnectionId, player.LapCount, lapTimeFormatted, cuts, splinePosition);

        player.HasFinishedLap = true;

        // Determine round result based on roles and positions
        RoundResult roundResult = DetermineRoundResult(player);

        // Handle all results, including draws
        HandleRoundComplete(roundResult, player);

        // Send lap result packet
        var resultPacket = new LapResultPacket
        {
            LapTimeMs = (int)lapTimeMs,
            LapTimeFormatted = lapTimeFormatted,
            Cuts = cuts,
            Collisions = 0,
            Saved = false,
            Reason = "Cat & Mouse Racing"
        };

        _logger.LogInformation("[TCAM] Sending lap result to {ClientName}", client.Name);
        client.SendPacket(resultPacket);
    }


    private RoundResult DetermineRoundResult(PlayerState finishedPlayer)
    {
        if (finishedPlayer.Role == RoleType.Mouse)
        {
            // Mouse finished - check if cat is close enough
            if (_currentCat != null)
            {
                var mouseSpline = finishedPlayer.SplinePosition;
                var catSpline = _currentCat.SplinePosition;

                // Calculate distance
                var distance = Math.Abs(catSpline - mouseSpline);
                if (distance > 0.5f)
                {
                    distance = 1.0f - distance;
                }

                _logger.LogInformation("[TCAM] Mouse finished - Cat: {CatSpline:F4}, Mouse: {MouseSpline:F4}, Distance: {Distance:F4}",
                    catSpline, mouseSpline, distance);

                // If cat is within chase distance, it's a draw
                if (distance <= _config.MaxChaseDistance)
                {
                    _logger.LogInformation("[TCAM] DRAW - Cat is within chase distance ({Distance:F3} <= {MaxDistance:F3})",
                        distance, _config.MaxChaseDistance);
                    return RoundResult.Draw;
                }
                else
                {
                    // Mouse wins - cat too far behind
                    _logger.LogInformation("[TCAM] MOUSE WINS - Cat too far behind ({Distance:F3} > {MaxDistance:F3})",
                        distance, _config.MaxChaseDistance);
                    return RoundResult.MouseWins;
                }
            }
            else
            {
                // No cat found, mouse wins by default
                return RoundResult.MouseWins;
            }
        }
        else if (finishedPlayer.Role == RoleType.Cat)
        {
            // Cat finished first - cat wins
            _logger.LogInformation("[TCAM] CAT WINS - Cat finished first");
            return RoundResult.CatWins;
        }

        return RoundResult.Draw;
    }


    private void HandleRoundComplete(RoundResult roundResult, PlayerState finishedPlayer)
    {
        _gameState = GameState.RoundComplete;
        _hasRoundBeenCompleted = true;

        string reason = "";
        PlayerState? winnerPlayer = null;

        switch (roundResult)
        {
            case RoundResult.CatWins:
                winnerPlayer = _currentCat;
                if (winnerPlayer != null) winnerPlayer.Wins++;
                reason = $"{winnerPlayer?.Name} caught the mouse!";
                break;
            case RoundResult.MouseWins:
                winnerPlayer = _currentMouse;
                if (winnerPlayer != null) winnerPlayer.Wins++;
                reason = $"{winnerPlayer?.Name} escaped successfully!";
                break;
            case RoundResult.Draw:
                reason = "Draw - Chase was too close when lead finished!";
                _logger.LogInformation("[TCAM] Draw occurred - no points awarded");
                break;
        }

        _logger.LogInformation("[TCAM] Round {Round} complete - Result: {Result} - Reason: {Reason} - Score: Cat {CatWins} - Mouse {MouseWins}",
            _currentRound, roundResult, reason, _currentCat?.Wins ?? 0, _currentMouse?.Wins ?? 0);

        // Send round result to all players
        SendRoundResultToAllPlayers(roundResult, reason, winnerPlayer);

        // Check if game is complete (first to 2 wins)
        if (winnerPlayer != null && winnerPlayer.Wins >= 2)
        {
            _logger.LogInformation("[TCAM] GAME COMPLETE triggered - {WinnerName} has {Wins} wins", winnerPlayer.Name, winnerPlayer.Wins);
            HandleGameComplete(winnerPlayer, "Normal game completion");
        }
        else
        {
            // Continue to next round
            _currentRound++;

            _logger.LogInformation("[TCAM] Continuing to round {NextRound} - Current score: Cat {CatWins} - Mouse {MouseWins}",
                _currentRound, _currentCat?.Wins ?? 0, _currentMouse?.Wins ?? 0);

            // Switch roles for next round
            SwitchRoles();

            // Delay before next round
            Task.Delay(_config.RoundDelaySeconds * 1000).ContinueWith(_ =>
            {
                _gameState = GameState.InProgress;
                StartNewRound();
            });
        }
    }


    private void SwitchRoles()
    {
        if (_currentCat != null && _currentMouse != null)
        {
            // Switch the roles
            var tempRole = _currentCat.Role;
            _currentCat.Role = _currentMouse.Role;
            _currentMouse.Role = tempRole;

            // Swap the current cat and mouse references
            (_currentCat, _currentMouse) = (_currentMouse, _currentCat);

            _logger.LogInformation("[TCAM] Roles switched - Cat: {Cat} [ID:{CatId}], Mouse: {Mouse} [ID:{MouseId}]",
                _currentCat.Name, _currentCat.ConnectionId, _currentMouse.Name, _currentMouse.ConnectionId);
        }
    }


    private void HandleGameComplete(PlayerState winner, string reason = "Normal game completion")
    {
        _gameState = GameState.GameComplete;
        _logger.LogInformation("[TCAM] GAME COMPLETE - Winner: {Winner} ({Wins} wins) after {Rounds} rounds - Reason: {Reason}",
            winner.Name, winner.Wins, _currentRound, reason);

        SendGameCompleteToAllPlayers(winner);

        // Send webhook notification
        _ = Task.Run(() => SendWebhookNotification(winner, reason));

        BroadcastChatMessage($"GAME OVER! {winner.Name.ToUpper()} WINS THE CAT & MOUSE CHAMPIONSHIP! ({winner.Wins}-{(winner == _currentCat ? _currentMouse?.Wins : _currentCat?.Wins)} after {_currentRound} rounds)");

        // End game after configured delay
        Task.Delay(_config.GameCompleteDelaySeconds * 1000).ContinueWith(_ => EndGame());
    }

    private async Task SendWebhookNotification(PlayerState winner, string reason)
    {
        if (!_config.EnableWebhook)
        {
            _logger.LogDebug("[TCAM] Webhook disabled in configuration, skipping notification");
            return;
        }
        
        if (string.IsNullOrEmpty(_config.WebhookUrl) || string.IsNullOrEmpty(_config.WebhookPassword))
        {
            _logger.LogInformation("[TCAM] Webhook not configured (missing URL or password), skipping notification");
            return;
        }

        try
        {
            // Get opponent
            var opponent = winner == _currentCat ? _currentMouse : _currentCat;
            if (opponent == null)
            {
                _logger.LogWarning("[TCAM] Cannot send webhook - opponent not found");
                return;
            }
            
            var payload = new WebhookPayload
            {
                PlayerName = winner.Name,
                SteamId = winner.Guid.ToString(),
                UserScore = winner.Wins,
                OpponentScore = opponent.Wins,
                OpponentName = opponent.Name,
                OpponentSteamId = opponent.Guid.ToString(),
                RoundsPlayed = _currentRound,
                GameEndTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                Password = _config.WebhookPassword
            };

            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = false
            });

            var content = new StringContent(json, Encoding.UTF8, "application/json");

            _logger.LogInformation("[TCAM] Sending webhook notification for winner: {PlayerName} ({SteamId}) vs {OpponentName} ({OpponentSteamId}) to {Url}", 
                winner.Name, winner.Guid, opponent.Name, opponent.Guid, _config.WebhookUrl);

            var response = await _httpClient.PostAsync(_config.WebhookUrl, content);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("[TCAM] Webhook notification sent successfully");
            }
            else
            {
                var responseContent = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("[TCAM] Webhook notification failed with status {StatusCode}: {Response}", 
                    response.StatusCode, responseContent);
            }
        }
        catch (TaskCanceledException)
        {
            _logger.LogWarning("[TCAM] Webhook notification timed out after {Timeout} seconds", _config.WebhookTimeoutSeconds);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TCAM] Failed to send webhook notification");
        }
    }

    private void EndGame()
    {
        _gameActive = false;
        _gameState = GameState.Waiting;
        _currentRound = 0;
        _hasRoundBeenCompleted = false;
        
        // Reset all players
        foreach (var player in _players.Values)
        {
            player.Role = RoleType.None;
            player.Wins = 0;
            player.HasFinishedLap = false;
            player.LapCount = 0;
        }
        
        _currentCat = null;
        _currentMouse = null;
        
        _logger.LogInformation("[TCAM] Game ended and reset");
        SendGameStateToAllPlayers();
        BroadcastChatMessage("Game ended. Type /startgame to play again!");
    }

    // Send targeted packets to each player
    private void SendGameStateToAllPlayers()
    {
        foreach (var car in _entryCarManager.ConnectedCars.Values)
        {
            if (car.Client != null && _players.TryGetValue(car.Client.SessionId, out var player))
            {
                var packet = new GameStatePacket
                {
                    State = (int)_gameState,
                    CurrentRound = _currentRound,
                    MyRole = (int)player.Role,
                    CatName = _currentCat?.Name ?? "",
                    MouseName = _currentMouse?.Name ?? "",
                    CatWins = _currentCat?.Wins ?? 0,
                    MouseWins = _currentMouse?.Wins ?? 0
                };

                car.Client.SendPacket(packet);
                _logger.LogDebug("[TCAM] Sent GameState to {ClientName} with role {Role}", 
                    car.Client.Name, player.Role);
            }
        }
    }

    private void SendRoundStartToAllPlayers()
    {
        foreach (var car in _entryCarManager.ConnectedCars.Values)
        {
            if (car.Client != null && _players.TryGetValue(car.Client.SessionId, out var player))
            {
                var packet = new RoundStartPacket
                {
                    Round = _currentRound,
                    MyRole = (int)player.Role,
                    CatName = _currentCat?.Name ?? "",
                    MouseName = _currentMouse?.Name ?? ""
                };

                car.Client.SendPacket(packet);
                _logger.LogDebug("[TCAM] Sent RoundStart to {ClientName} with role {Role}", 
                    car.Client.Name, player.Role);
            }
        }
    }

    private void SendRoundResultToAllPlayers(RoundResult roundResult, string reason, PlayerState? winnerPlayer)
    {
        foreach (var car in _entryCarManager.ConnectedCars.Values)
        {
            if (car.Client != null && _players.TryGetValue(car.Client.SessionId, out var player))
            {
                bool didIWin = false;
                if (roundResult == RoundResult.CatWins && player.Role == RoleType.Cat)
                    didIWin = true;
                else if (roundResult == RoundResult.MouseWins && player.Role == RoleType.Mouse)
                    didIWin = true;
                // Draw = nobody wins, didIWin stays false

                var packet = new RoundResultPacket
                {
                    Winner = (int)roundResult, // 0=CatWins, 1=MouseWins, 2=Draw
                    Reason = reason,
                    Round = _currentRound,
                    DidIWin = didIWin,
                    WinnerName = winnerPlayer?.Name ?? "Draw"
                };

                car.Client.SendPacket(packet);
                _logger.LogInformation("[TCAM] Sent RoundResult to {ClientName} - Result: {Result} ({ResultInt}), DidIWin: {DidIWin}, Winner: {WinnerName}", 
                    car.Client.Name, roundResult, (int)roundResult, didIWin, packet.WinnerName);
            }
        }
    }

    private void SendGameCompleteToAllPlayers(PlayerState winner)
    {
        foreach (var car in _entryCarManager.ConnectedCars.Values)
        {
            if (car.Client != null && _players.TryGetValue(car.Client.SessionId, out var player))
            {
                var packet = new GameCompletePacket
                {
                    WinnerName = winner.Name,
                    WinCount = winner.Wins,
                    DidIWin = player.ConnectionId == winner.ConnectionId
                };

                car.Client.SendPacket(packet);
                _logger.LogDebug("[TCAM] Sent GameComplete to {ClientName} - DidIWin: {DidIWin}", 
                    car.Client.Name, packet.DidIWin);
            }
        }
    }
}