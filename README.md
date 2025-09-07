Gunma Legends discord: https://discord.com/invite/Gj2Kg4y9yJ
Future updates coming soon obviously, this is a very early version. At the time of creation it works on the latest AssettoServer prerelease.
A plugin that automates cat and mouse racing on assetto corsa, used on the gunma legends servers.
This was built as more of an experiment if you want a full fledged plugin that does all this does and more please checkout Ukkos plugin here:
https://github.com/EricManintveld/TougePlugin/wiki/Touge-Plugin
First ever plugin written by with the help of some AI models, as my knowladge of C# is basically zero.

Only works on maps with set end lines with servers with the race session enabled.
Uses the actual assetto corsa race mode for the countdown start.
Online script and leaderboard website not included im sure you could do it better than i have.

Meant to work alongside a website that recieves post requests to save data. The requests look like this:

{
  "playerName": "WinnerUsername",
  "steamId": "76561198123456789",
  "userScore": 2,
  "opponentScore": 1,
  "opponentName": "OpponentUsername", 
  "opponentSteamId": "76561198987654321",
  "roundsPlayed": 3,
  "gameEndTime": 1642694445,
  "password": "your_configured_password"
}

Teleport positions you have to take the actual teleport position and a forward vector to decide rotation with the ingame object inspector.

Please contact me at fizzy5878 for any support regarding this plugin and such.
I also take requests if theres any interest for that!
