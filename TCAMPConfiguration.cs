using System.Collections.Generic;
using System.Numerics;
using JetBrains.Annotations;
using YamlDotNet.Serialization;

namespace TCAMPlugin;

[UsedImplicitly(ImplicitUseKindFlags.Assign, ImplicitUseTargetFlags.WithMembers)]
public class TCAMPluginConfiguration
{
    /// <summary>
    /// Maximum distance the mouse can be behind the cat before losing (0.0 to 1.0, where 1.0 is full track length)
    /// </summary>
    [YamlMember(Description = "Maximum distance the mouse can be behind the cat before losing (0.0 to 1.0)")]
    public float MaxChaseDistance { get; set; } = 0.15f; // 15% of track length
    
    /// <summary>
    /// Whether to automatically assign roles or let players choose
    /// </summary>
    [YamlMember(Description = "Whether to automatically assign roles or let players choose")]
    public bool AutoAssignRoles { get; set; } = true;
    
    /// <summary>
    /// Minimum number of players required to start a game
    /// </summary>
    [YamlMember(Description = "Minimum number of players required to start a game")]
    public int MinPlayersToStart { get; set; } = 2;
    
    /// <summary>
    /// Delay between rounds in seconds
    /// </summary>
    [YamlMember(Description = "Delay between rounds in seconds")]
    public int RoundDelaySeconds { get; set; } = 5;
    
    /// <summary>
    /// Delay after game complete before reset in seconds
    /// </summary>
    [YamlMember(Description = "Delay after game complete before reset in seconds")] 
    public int GameCompleteDelaySeconds { get; set; } = 10;
    
    /// <summary>
    /// Cat starting position (x, y, z coordinates)
    /// </summary>
    [YamlMember(Description = "Cat starting position (x, y, z coordinates)")]
    public List<float> CatStartPosition { get; set; } = new List<float> { -672.35f, 800.69f, 1200.43f };
    
    /// <summary>
    /// Cat forward direction point (x, y, z coordinates) - used to calculate direction vector
    /// </summary>
    [YamlMember(Description = "Cat forward direction point (x, y, z coordinates) - used to calculate direction vector")]
    public List<float> CatForwardPosition { get; set; } = new List<float> { -672.35f, 800.69f, 1300.43f };
    
    /// <summary>
    /// Mouse starting position (x, y, z coordinates)
    /// </summary>
    [YamlMember(Description = "Mouse starting position (x, y, z coordinates)")]
    public List<float> MouseStartPosition { get; set; } = new List<float> { -600.0f, 800.69f, 1200.43f };
    
    /// <summary>
    /// Mouse forward direction point (x, y, z coordinates) - used to calculate direction vector
    /// </summary>
    [YamlMember(Description = "Mouse forward direction point (x, y, z coordinates) - used to calculate direction vector")]
    public List<float> MouseForwardPosition { get; set; } = new List<float> { -600.0f, 800.69f, 1300.43f };
    
    /// <summary>
    /// Delay in seconds before teleporting players after session restart
    /// </summary>
    [YamlMember(Description = "Delay in seconds before teleporting players after session restart")]
    public int TeleportDelaySeconds { get; set; } = 3;
    
    /// <summary>
    /// Whether to enable player teleportation at round start
    /// </summary>
    [YamlMember(Description = "Whether to enable player teleportation at round start")]
    public bool EnableTeleport { get; set; } = true;

    /// <summary>
    /// Whether to enable webhook notifications when a player wins
    /// </summary>
    [YamlMember(Description = "Whether to enable webhook notifications when a player wins")]
    public bool EnableWebhook { get; set; } = true;

    /// <summary>
    /// URL to send POST request when a player wins the game
    /// </summary>
    [YamlMember(Description = "URL to send POST request when a player wins the game")]
    public string? WebhookUrl { get; set; }
    
    /// <summary>
    /// Password/API key for webhook authentication
    /// </summary>
    [YamlMember(Description = "Password/API key for webhook authentication")]
    public string? WebhookPassword { get; set; }
    
    /// <summary>
    /// Timeout for webhook requests in seconds
    /// </summary>
    [YamlMember(Description = "Timeout for webhook requests in seconds")]
    public int WebhookTimeoutSeconds { get; set; } = 10;

    // Helper methods to convert lists to Vector3
    public Vector3 GetCatStartVector3() => new Vector3(CatStartPosition[0], CatStartPosition[1], CatStartPosition[2]);
    public Vector3 GetCatForwardVector3() => new Vector3(CatForwardPosition[0], CatForwardPosition[1], CatForwardPosition[2]);
    public Vector3 GetMouseStartVector3() => new Vector3(MouseStartPosition[0], MouseStartPosition[1], MouseStartPosition[2]);
    public Vector3 GetMouseForwardVector3() => new Vector3(MouseForwardPosition[0], MouseForwardPosition[1], MouseForwardPosition[2]);
    
    public Vector3 GetCatDirectionVector() 
    {
        var direction = GetCatStartVector3() - GetCatForwardVector3();
        return direction.Length() > 0 ? Vector3.Normalize(direction) : Vector3.UnitZ;
    }
    
    public Vector3 GetMouseDirectionVector() 
    {
        var direction = GetMouseStartVector3() - GetMouseForwardVector3();
        return direction.Length() > 0 ? Vector3.Normalize(direction) : Vector3.UnitZ;
    }
}