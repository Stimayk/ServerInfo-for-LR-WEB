using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API;
using System.Text.RegularExpressions;
using System.Text;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Core.Attributes.Registration;

namespace ServerInfo
{
    public partial class ServerInfo : BasePlugin
    {
        public override string ModuleName => "ServerInfo for LR WEB";
        public override string ModuleAuthor => "E!N";
        public override string ModuleVersion => "1.1";

        private const int MaxPlayers = 65;
        private string server = "";
        private string password = "";
        private string url = "";
        private float timerInterval = 40.0f;

        public Dictionary<int, PlayerInfo> PlayerList { get; set; } = new();

        public override void Load(bool hotReload)
        {
            LoadCfg();
            var ip = GetIP();
            AddCommand("css_getserverinfo", "Get server info", (player, info) => UpdatePlayerInfo());
            AddTimer(timerInterval, UpdatePlayerInfo, TimerFlags.REPEAT);

            RegisterListener<Listeners.OnClientAuthorized>((slot, steamid) =>
            {
                CCSPlayerController? player = Utilities.GetPlayerFromSlot(slot);
                if (!IsPlayerValid(player)) return;
                AddTimer(1.0f, () =>
                {
                    InitPlayers(player);
                });
            });
        }

        private void LoadCfg()
        {
            try
            {
                var filePath = GetConfigFilePath();
                if (File.Exists(filePath))
                {
                    ParseConfigFile(filePath);
                }
                else
                {
                    Console.WriteLine("Config file not found.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading configuration: {ex.Message}");
            }
        }

        private string GetConfigFilePath()
        {
            var moduleDirectoryParent = Directory.GetParent(ModuleDirectory) ?? throw new InvalidOperationException("Module directory parent is null");
            var parentDirectory = moduleDirectoryParent.Parent ?? throw new InvalidOperationException("Parent directory is null");
            return Path.Combine(parentDirectory.FullName, "configs/server_info.ini");
        }

        private void ParseConfigFile(string filePath)
        {
            var lines = File.ReadAllLines(filePath);
            bool insideServerBlock = false;

            foreach (var line in lines)
            {
                if (line.Trim().StartsWith("\"Server\""))
                {
                    insideServerBlock = true;
                    continue;
                }

                if (insideServerBlock)
                {
                    if (line.Contains('}'))
                    {
                        insideServerBlock = false;
                        continue;
                    }

                    var match = MyRegex().Match(line);
                    if (match.Success && match.Groups.Count == 3)
                    {
                        var key = match.Groups[1].Value.Trim();
                        var value = match.Groups[2].Value.Trim();

                        if (key == "server_info") server = value;
                        else if (key == "password") password = value;
                        else if (key == "url") url = value;
                        else if (key == "timer_interval" && float.TryParse(value, out float interval)) timerInterval = interval;
                    }
                }
            }
        }

        private static string GetIP()
        {
            var serverIp = ConVar.Find("ip")?.StringValue ?? "Unknown IP";
            var serverPort = ConVar.Find("hostport")?.GetPrimitiveValue<int>().ToString() ?? "Unknown Port";
            return $"{serverIp}:{serverPort}";
        }

        [GameEventHandler]
        private HookResult OnPlayerDisconnect(EventPlayerDisconnect @event, GameEventInfo info)
        {
            CCSPlayerController _player = @event.Userid;

            if (!IsPlayerValid(_player)) return HookResult.Continue;
            if (PlayerList.TryGetValue(_player.Slot, out var theplayer))
            {
                PlayerList.Remove(_player.Slot);
            }
            return HookResult.Continue;
        }

        public void InitPlayers(CCSPlayerController player)
        {
            if (!IsPlayerValid(player)) return;

            PlayerInfo playerInfo = new()
            {
                UserId = player.UserId,
                SteamId = player.AuthorizedSteamID?.SteamId64.ToString(),
                Name = player.PlayerName,
                Kills = player.ActionTrackingServices?.MatchStats.Kills ?? 0,
                Deaths = player.ActionTrackingServices?.MatchStats.Deaths ?? 0,
                Assists = player.ActionTrackingServices?.MatchStats.Assists ?? 0,
                Headshots = player.ActionTrackingServices?.MatchStats.HeadShotKills ?? 0
            };

            PlayerList[player.Slot] = playerInfo;
        }

        private void UpdatePlayerInfo()
        {
            foreach (var playerInfo in PlayerList.Values)
            {
                if (playerInfo.UserId != null)
                {
                    var player = Utilities.GetPlayerFromUserid((int)playerInfo.UserId);
                    if (player != null && IsPlayerValid(player))
                    {
                        playerInfo.Kills = player.ActionTrackingServices?.MatchStats.Kills ?? 0;
                        playerInfo.Deaths = player.ActionTrackingServices?.MatchStats.Deaths ?? 0;
                        playerInfo.Assists = player.ActionTrackingServices?.MatchStats.Assists ?? 0;
                        playerInfo.Headshots = player.ActionTrackingServices?.MatchStats.HeadShotKills ?? 0;
                    }
                    if (!string.IsNullOrEmpty(server))
                    {
                        SendServerInfo(playerInfo);
                    }
                }
            }
        }

        private static (int t1score, int t2score) GetTeamsScore()
        {
            var teamEntities = Utilities.FindAllEntitiesByDesignerName<CCSTeam>("cs_team_manager");
            var t1score = teamEntities.FirstOrDefault(t => t.Teamname == "CT")?.Score ?? 0;
            var t2score = teamEntities.FirstOrDefault(t => t.Teamname == "TERRORIST")?.Score ?? 0;
            return (t1score, t2score);
        }

        private void SendServerInfo(PlayerInfo playerinfo)
        {
            if (string.IsNullOrEmpty(server) || string.IsNullOrEmpty(url) || string.IsNullOrEmpty(password))
            {
                return;
            }

            var jsonPlayers = GetPlayersJson(playerinfo);
            var (t1score, t2score) = GetTeamsScore();
            var jsonData = new { score_ct = t1score, score_t = t2score, players = jsonPlayers };
            var jsonString = System.Text.Json.JsonSerializer.Serialize(jsonData);
            PostData(jsonString);
        }

        private static List<object> GetPlayersJson(PlayerInfo playerinfo)
        {
            var jsonPlayers = new List<object>
            {
                new
                {
                    name = playerinfo.Name ?? "Unknown",
                    steamid = playerinfo.SteamId?.ToString(),
                    kills = playerinfo.Kills.ToString(),
                    assists = playerinfo.Deaths.ToString(),
                    death = playerinfo.Assists.ToString(),
                    headshots = playerinfo.Headshots.ToString(),
                    rank = "0",
                    playtime = 0
                }
            };
            return jsonPlayers;
        }

        private void PostData(string jsonData)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, $"{url}/app/modules/module_block_main_monitoring_rating/forward/js_controller.php?server={server}&password={password}")
            {
                Content = new StringContent(jsonData, Encoding.UTF8, "application/json")
            };

            using var httpClient = new HttpClient();
            try
            {
                var response = httpClient.Send(request);
                var responseContent = response.Content.ReadAsStringAsync().Result;

                if (!response.IsSuccessStatusCode)
                {
                    Server.PrintToConsole("Ошибка при отправке запроса.");
                }
            }
            catch (Exception ex)
            {
                Server.PrintToConsole($"Exception in HTTP request: {ex.Message}");
            }

        }

        public static bool IsPlayerValid(CCSPlayerController? player)
        {
            return (player is
            {
                IsValid: true, IsBot: false, IsHLTV: false
            }); ;
        }

        [GeneratedRegex("\"([^\"]+)\"\\s+\"([^\"]+)\"")]
        private static partial Regex MyRegex();
    }

    public class PlayerInfo
    {
        public int? UserId { get; set; }
        public string? SteamId { get; set; }
        public string? Name { get; set; }
        public int? Kills { get; set; } = 0;
        public int? Deaths { get; set; } = 0;
        public int? Assists { get; set; } = 0;
        public int? Headshots { get; set; } = 0;
    }
}