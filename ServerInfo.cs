using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API;
using System.Text.RegularExpressions;
using System.Text;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using MySqlConnector;
using Newtonsoft.Json;
using CounterStrikeSharp.API.Modules.Timers;

namespace ServerInfo
{
    public partial class ServerInfo : BasePlugin
    {
        public override string ModuleName => "ServerInfo for LR WEB";
        public override string ModuleAuthor => "E!N";
        public override string ModuleVersion => "1.3";

        private bool isDebugMode = false;

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
            AddCommand("css_reloadserverinfo", "Forced to read the cfg", (player, info) => LoadCfg());
            AddTimer(timerInterval, UpdatePlayerInfo, TimerFlags.REPEAT);

            RegisterListener<Listeners.OnClientAuthorized>((slot, steamid) =>
            {
                CCSPlayerController? player = Utilities.GetPlayerFromSlot(slot);
                if (!IsPlayerValid(player)) return;
                AddTimer(1.0f, () =>
                {
                    InitPlayers(player);
                    if (PlayerList.TryGetValue(player.Slot, out var playerInfo))
                    {
                        playerInfo.SessionStartTime = DateTime.Now;
                    }
                });
                LogDebug("Player authorized: " + steamid);
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
                LogDebug("Configuration loaded successfully.");
            }
            catch (Exception ex)
            {
                LogDebug($"Error loading configuration: {ex.Message}");
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
            LogDebug("Parsing configuration file...");
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
                        else if (key == "debug_mode") isDebugMode = value.ToLower() == "true";
                        LogDebug($"Parsed key: {key}, value: {value}");

                    }
                }
            }
            LogDebug("Configuration file parsed successfully.");
        }

        private string GetIP()
        {
            LogDebug("Fetching server IP and port...");
            var serverIp = ConVar.Find("ip")?.StringValue ?? "Unknown IP";
            var serverPort = ConVar.Find("hostport")?.GetPrimitiveValue<int>().ToString() ?? "Unknown Port";
            string ipPort = $"{serverIp}:{serverPort}";
            LogDebug($"Server IP and port: {ipPort}");
            return ipPort;
        }

        public int GetRankFromDatabase(string? steamid)
        {
            LogDebug("Fetching rank from database for Steam ID: " + steamid);

            var dbConfig = LoadDbConfig();
            if (dbConfig == null)
            {
                LogDebug("Database configuration not found.");
                return 0;
            }

            var connectionString = new MySqlConnectionStringBuilder
            {
                Server = dbConfig.DbHost,
                UserID = dbConfig.DbUser,
                Password = dbConfig.DbPassword,
                Database = dbConfig.DbName,
                Port = uint.Parse(dbConfig.DbPort),
            }.ToString();

            using var connection = new MySqlConnection(connectionString);
            try
            {
                connection.Open();

                var query = $"SELECT rank FROM {dbConfig.Name} WHERE steam = @SteamId";
                using var command = new MySqlCommand(query, connection);
                command.Parameters.AddWithValue("@SteamId", steamid);
                var result = command.ExecuteScalar();
                if (result != null)
                {
                    LogDebug("Rank fetched successfully: " + result);
                    return Convert.ToInt32(result);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error when connecting to the database: " + ex.Message);
                LogDebug("Database connection error: " + ex.Message);
            }

            return 0;
        }

        private DbConfig? LoadDbConfig()
        {
            string firstConfigFilePath = Path.Combine(Directory.GetParent(ModuleDirectory)?.Parent?.FullName ?? "", "plugins/RanksPoints/dbconfig.json");
            if (File.Exists(firstConfigFilePath))
            {
                LogDebug("Loading primary dbconfig from: " + firstConfigFilePath);
                return JsonConvert.DeserializeObject<DbConfig>(File.ReadAllText(firstConfigFilePath));
            }
            else
            {
                LogDebug("Primary dbconfig not found at: " + firstConfigFilePath);
            }

            string secondConfigFilePath = Path.Combine(Directory.GetParent(ModuleDirectory)?.Parent?.FullName ?? "", "plugins/Ranks/settings_ranks.json");
            if (File.Exists(secondConfigFilePath))
            {
                LogDebug("Loading alternative config from: " + secondConfigFilePath);
                var alternativeConfig = JsonConvert.DeserializeObject<AlternativeConfig>(File.ReadAllText(secondConfigFilePath));
                if (alternativeConfig?.Connection != null)
                {
                    return new DbConfig
                    {
                        DbHost = alternativeConfig.Connection.Host,
                        DbUser = alternativeConfig.Connection.User,
                        DbPassword = alternativeConfig.Connection.Password,
                        DbName = alternativeConfig.Connection.Database,
                        DbPort = "3306",
                        Name = alternativeConfig.TableName
                    };
                }
                else
                {
                    LogDebug("Alternative config loaded but connection details are missing.");
                }
            }
            else
            {
                LogDebug("Alternative config not found at: " + secondConfigFilePath);
            }

            LogDebug("No valid database configuration found.");
            return null;
        }

        [GameEventHandler]
        public HookResult OnPlayerDisconnect(EventPlayerDisconnect @event, GameEventInfo info)
        {
            CCSPlayerController _player = @event.Userid;
            if (!IsPlayerValid(_player)) return HookResult.Continue;
            PlayerList.Remove(_player.Slot);
            LogDebug("Player disconnected: " + _player.UserId);
            return HookResult.Continue;
        }

        public void InitPlayers(CCSPlayerController player)
        {
            if (!IsPlayerValid(player)) return;
            LogDebug($"Initializing player with slot {player.Slot}");

            string? steamId2 = null;
            if (player.AuthorizedSteamID != null)
            {
                steamId2 = ConvertToSteam2((long)player.AuthorizedSteamID.SteamId64);
            }

            PlayerInfo playerInfo = new()
            {
                UserId = player.UserId,
                SteamId = player.AuthorizedSteamID?.SteamId64.ToString(),
                SteamId2 = steamId2,
                Name = player.PlayerName,
                Kills = player.ActionTrackingServices?.MatchStats.Kills ?? 0,
                Deaths = player.ActionTrackingServices?.MatchStats.Deaths ?? 0,
                Assists = player.ActionTrackingServices?.MatchStats.Assists ?? 0,
                Headshots = player.ActionTrackingServices?.MatchStats.HeadShotKills ?? 0
            };

            PlayerList[player.Slot] = playerInfo;
            LogDebug($"Player initialized: {playerInfo.Name}");
        }

        private string ConvertToSteam2(long steamId64)
        {
            long steamId3 = steamId64 - 76561197960265728;
            int y = (int)(steamId3 % 2);
            long z = steamId3 / 2;

            return $"STEAM_1:{y}:{z}";
        }

        private int CalculatePlayTime(PlayerInfo playerInfo)
        {
            LogDebug($"Calculating play time for player: {playerInfo.Name}");
            TimeSpan timeSpent = DateTime.Now - playerInfo.SessionStartTime;
            int playTime = (int)timeSpent.TotalSeconds;
            LogDebug($"Play time for {playerInfo.Name}: {playTime} seconds");
            return playTime;
        }
        private void LogDebug(string message)
        {
            if (isDebugMode)
            {
                Console.WriteLine(" ServerInfo | " + message);
            }
        }

        private void UpdatePlayerInfo()
        {
            Server.PrintToChatAll("1");
            LogDebug("Updating player info for all players...");
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
                LogDebug($"Updated player info for player: {playerInfo.Name}");
            }
            LogDebug("All player info updated.");
            Server.PrintToChatAll("2");
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
            LogDebug("Preparing to send server info...");
            if (string.IsNullOrEmpty(server) || string.IsNullOrEmpty(url) || string.IsNullOrEmpty(password))
            {
                return;
            }
            LogDebug($"Server info for player {playerinfo.Name} prepared for sending.");

            List<object> jsonPlayers;
            if (playerinfo != null)
            {
                jsonPlayers = GetPlayersJson(playerinfo);
            }
            else
            {
                jsonPlayers = new List<object>();
            }

            var (t1score, t2score) = GetTeamsScore();
            var jsonData = new { score_ct = t1score, score_t = t2score, players = jsonPlayers };
            var jsonString = System.Text.Json.JsonSerializer.Serialize(jsonData);

            Task.Run(async () => await PostData(jsonString)).Wait();
            LogDebug("Server info sent.");
        }

        private List<object> GetPlayersJson(PlayerInfo playerinfo)
        {
            var playTime = CalculatePlayTime(playerinfo);
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
                    rank = GetRankFromDatabase(playerinfo.SteamId2),
                    playtime = playTime
                }
            };
            return jsonPlayers;
        }

        private async Task PostData(string jsonData)
        {
            LogDebug("Sending data to server...");
            var request = new HttpRequestMessage(HttpMethod.Post, $"{url}/app/modules/module_block_main_monitoring_rating/forward/js_controller.php?server={server}&password={password}")
            {
                Content = new StringContent(jsonData, Encoding.UTF8, "application/json")
            };

            using var httpClient = new HttpClient();
            try
            {
                var response = await httpClient.SendAsync(request);
                var responseContent = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine("Error when sending a request.");
                }
                LogDebug("Data sent. Response: " + responseContent);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Exception in HTTP request: {ex.Message}");
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
        public string? SteamId2 { get; set; }
        public string? Name { get; set; }
        public int? Kills { get; set; } = 0;
        public int? Deaths { get; set; } = 0;
        public int? Assists { get; set; } = 0;
        public int? Headshots { get; set; } = 0;
        public DateTime SessionStartTime { get; set; }
    }

    public class DbConfig
    {
        public required string DbHost { get; set; }
        public required string DbUser { get; set; }
        public required string DbPassword { get; set; }
        public required string DbName { get; set; }
        public required string DbPort { get; set; }
        public required string Name { get; set; }
    }

    public class AlternativeConfig
    {
        public required string TableName { get; set; }
        public required ConnectionConfig Connection { get; set; }
    }

    public class ConnectionConfig
    {
        public required string Host { get; set; }
        public required string Database { get; set; }
        public required string User { get; set; }
        public required string Password { get; set; }
    }
}