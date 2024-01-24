using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API;
using System.Text.RegularExpressions;
using System.Text;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using MySqlConnector;
using Newtonsoft.Json;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Entities;
using System.ComponentModel.DataAnnotations;

namespace ServerInfo
{
    public partial class ServerInfo : BasePlugin
    {
        public override string ModuleName => "ServerInfo for LR WEB";
        public override string ModuleAuthor => "E!N";
        public override string ModuleVersion => "1.4.1";

        private bool isDebugMode = false;

        private const int MaxPlayers = 65;
        public string Server { get; private set; } = "";
        public string Password { get; private set; } = "";
        public string Url { get; private set; } = "";
        private float timerInterval = 40.0f;
        private int statisticType = 0;

        private const long SteamId64Base = 76561197960265728;
        private const string LogPrefix = "ServerInfo |";
        private const string TeamCT = "CT";
        private const string TeamTerrorist = "TERRORIST";
        private readonly HttpClient httpClient = new()
        {
            Timeout = TimeSpan.FromSeconds(10)
        };
        private readonly Dictionary<string, int> rankCache = new Dictionary<string, int>();
        public Dictionary<int, PlayerInfo> PlayerList { get; private set; } = new Dictionary<int, PlayerInfo>();

        public override void Load(bool hotReload)
        {
            LoadCfg();
            GetIP();
            AddServerInfoCommands();
            ScheduleRegularUpdates();

            RegisterClientAuthListener();
        }

        private void AddServerInfoCommands()
        {
            AddCommand("css_getserverinfo", "Get server info",
                (player, info) => Task.Run(async () => await UpdatePlayerInfoAsync()));

            AddCommand("css_reloadserverinfo", "Forced to read the cfg",
                (player, info) => LoadCfg());
        }

        private void ScheduleRegularUpdates()
        {
            AddTimer(timerInterval,
                () => Task.Run(async () => await UpdatePlayerInfoAsync()), TimerFlags.REPEAT);
        }

        private void RegisterClientAuthListener()
        {
            LogDebug("Registering client authorization listener...");
            RegisterListener<Listeners.OnClientAuthorized>((slot, steamid) =>
            {
                LogDebug($"Client authorized event triggered. Slot: {slot}, SteamID: {steamid}");
                HandleClientAuthorization(slot, steamid);
            });
            LogDebug("Client authorization listener registered successfully.");
        }

        private void HandleClientAuthorization(int slot, SteamID steamid)
        {
            LogDebug($"Handling client authorization for slot {slot}, SteamID: {steamid}");
            CCSPlayerController? player = Utilities.GetPlayerFromSlot(slot);

            if (!IsPlayerValid(player))
            {
                LogDebug($"Invalid player in slot {slot}. Authorization skipped.");
                return;
            }

            LogDebug($"Valid player found in slot {slot}. Proceeding with session initialization.");
            Task.Run(() =>
            {
                CounterStrikeSharp.API.Server.NextFrame(() => InitPlayerSession(player, steamid));
            });
        }

        private void InitPlayerSession(CCSPlayerController player, SteamID steamid)
        {
            if (!PlayerList.TryGetValue(player.Slot, out PlayerInfo? playerInfo))
            {
                var steamId2 = player.AuthorizedSteamID != null
                    ? ConvertToSteam2((long)player.AuthorizedSteamID.SteamId64)
                    : null;

                playerInfo = new PlayerInfo
                {
                    UserId = player.UserId,
                    SteamId = player.AuthorizedSteamID?.SteamId64.ToString(),
                    SteamId2 = steamId2,
                    Name = player.PlayerName,
                    SessionStartTime = DateTime.Now,
                };

                PlayerList[player.Slot] = playerInfo;
                LogDebug($"Initialized new PlayerInfo for {player.PlayerName} with SessionStartTime: {playerInfo.SessionStartTime}");
            }
            else
            {
                UpdatePlayerStats(player, playerInfo);
                LogDebug($"Updated PlayerInfo for {player.PlayerName}");
            }
        }

        private void LoadCfg()
        {
            try
            {
                var filePath = GetConfigFilePath();
                if (CheckConfigFileExists(filePath))
                {
                    ParseAndLoadConfig(filePath);
                }
                else
                {
                    LogConfigFileNotFound();
                }
            }
            catch (Exception ex)
            {
                LogConfigLoadError(ex);
            }
        }

        private bool CheckConfigFileExists(string filePath)
        {
            if (!File.Exists(filePath))
            {
                Console.WriteLine("Config file not found.");
                return false;
            }
            return true;
        }

        private void ParseAndLoadConfig(string filePath)
        {
            ParseConfigFile(filePath);
            LogDebug("Configuration loaded successfully.");
        }

        private void LogConfigFileNotFound()
        {
            LogDebug("Configuration file not found.");
        }

        private void LogConfigLoadError(Exception ex)
        {
            LogDebug($"Error loading configuration: {ex.Message}");
        }

        private string GetConfigFilePath()
        {
            try
            {
                var moduleDirectoryParent = GetParentDirectory(ModuleDirectory, "Module directory");
                var parentDirectory = GetParentDirectory(moduleDirectoryParent.FullName, "Module directory parent");
                return Path.Combine(parentDirectory.FullName, "configs/server_info.ini");
            }
            catch (Exception ex)
            {
                LogDebug($"Error getting config file path: {ex.Message}");
                throw;
            }
        }

        private DirectoryInfo GetParentDirectory(string directoryPath, string directoryName)
        {
            var parentDirectory = Directory.GetParent(directoryPath);
            return parentDirectory ?? throw new InvalidOperationException($"{directoryName} parent is null");
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

                if (insideServerBlock && line.Contains('}'))
                {
                    insideServerBlock = false;
                    continue;
                }

                if (insideServerBlock)
                {
                    ProcessConfigLine(line);
                }
            }
            LogDebug("Configuration file parsed successfully.");
        }

        private void ProcessConfigLine(string line)
        {
            var match = ConfigLineRegex().Match(line);
            if (!match.Success || match.Groups.Count != 3) return;

            var key = match.Groups[1].Value.Trim();
            var value = match.Groups[2].Value.Trim();

            switch (key)
            {
                case "server_info":
                    Server = value;
                    break;
                case "password":
                    Password = value;
                    break;
                case "url":
                    Url = value;
                    break;
                case "timer_interval":
                    if (float.TryParse(value, out float interval)) timerInterval = interval;
                    break;
                case "debug_mode":
                    isDebugMode = value.ToLower() == "true";
                    break;
                case "statistic_type":
                    if (int.TryParse(value, out int type)) statisticType = type;
                    break;
            }
            LogDebug($"Parsed key: {key}, value: {value}");
        }

        private string GetIP()
        {
            LogDebug("Fetching server IP and port...");
            try
            {
                var serverIp = GetServerIP();
                var serverPort = GetServerPort();
                string ipPort = $"{serverIp}:{serverPort}";
                LogDebug($"Server IP and port: {ipPort}");
                return ipPort;
            }
            catch (Exception ex)
            {
                LogDebug($"Error fetching server IP and port: {ex.Message}");
                return "Unknown IP:Unknown Port";
            }
        }

        private string GetServerIP()
        {
            return ConVar.Find("ip")?.StringValue ?? "Unknown IP";
        }

        private string GetServerPort()
        {
            return ConVar.Find("hostport")?.GetPrimitiveValue<int>().ToString() ?? "Unknown Port";
        }



        public async Task<int> GetRankFromDatabaseAsync(string? steamid)
        {
            LogDebug("Fetching rank from database for Steam ID: " + steamid);

            if (steamid != null && rankCache.TryGetValue(steamid, out int cachedRank))
            {
                LogDebug($"Rank for {steamid} fetched from cache: {cachedRank}");
                return cachedRank;
            }

            var dbConfig = LoadDbConfig();
            if (dbConfig == null)
            {
                LogDebug("Database configuration not found.");
                return 0;
            }

            try
            {
                int rank = await ExecuteRankQueryAsync(steamid, dbConfig);
                if (steamid != null)
                {
                    rankCache[steamid] = rank;
                }
                return rank;
            }
            catch (Exception ex)
            {
                LogDatabaseError(ex);
                return 0;
            }
        }

        private async Task<int> ExecuteRankQueryAsync(string? steamid, DbConfig dbConfig)
        {
            var connectionString = BuildConnectionString(dbConfig);
            using var connection = new MySqlConnection(connectionString);
            await connection.OpenAsync();

            var query = $"SELECT rank FROM {dbConfig.Name} WHERE steam = @SteamId";
            using var command = new MySqlCommand(query, connection);
            command.Parameters.AddWithValue("@SteamId", steamid);
            var result = await command.ExecuteScalarAsync();

            if (result != null)
            {
                LogDebug("Rank fetched successfully: " + result);
                return Convert.ToInt32(result);
            }

            return 0;
        }

        private string BuildConnectionString(DbConfig dbConfig)
        {
            if (!uint.TryParse(dbConfig.DbPort, out uint port))
            {
                throw new ArgumentException("Invalid port number.");
            }

            return new MySqlConnectionStringBuilder
            {
                Server = dbConfig.DbHost,
                UserID = dbConfig.DbUser,
                Password = dbConfig.DbPassword,
                Database = dbConfig.DbName,
                Port = port
            }.ToString();
        }

        private void LogDatabaseError(Exception ex)
        {
            Console.WriteLine("Error when connecting to the database: " + ex.Message);
            LogDebug("Database connection error: " + ex.Message);
        }

        private DbConfig? LoadDbConfig()
        {
            var configFilePath = GetConfigFilePathForType(statisticType);

            if (string.IsNullOrEmpty(configFilePath))
            {
                LogDebug("Database not configured or disabled.");
                return null;
            }

            if (!File.Exists(configFilePath))
            {
                LogDebug("Dbconfig not found at: " + configFilePath);
                return null;
            }

            try
            {
                LogDebug("Loading dbconfig from: " + configFilePath);
                var configJson = File.ReadAllText(configFilePath);
                return JsonConvert.DeserializeObject<DbConfig>(configJson);
            }
            catch (Exception ex)
            {
                LogDebug($"Error loading dbconfig from {configFilePath}: {ex.Message}");
                return null;
            }
        }

        private string GetConfigFilePathForType(int type)
        {
            var parentDirectory = Directory.GetParent(ModuleDirectory)?.Parent?.FullName ?? "";

            return type switch
            {
                1 => Path.Combine(parentDirectory, "plugins/RanksPoints/dbconfig.json"),
                2 => Path.Combine(parentDirectory, "plugins/Ranks/settings_ranks.json"),
                //case 3: return ""; TO DO: LevelsRanks
                _ => "",
            };
        }

        [GameEventHandler]
        public HookResult OnPlayerConnect(EventPlayerConnectFull @event, GameEventInfo info)
        {
            var player = @event.Userid;

            if (!IsPlayerValid(player)) return HookResult.Continue;

            if (player.AuthorizedSteamID != null)
            {
                InitPlayerSession(player, player.AuthorizedSteamID);
            }
            else
            {
                LogDebug("Player's AuthorizedSteamID is null. Player session initialization skipped.");
            }

            return HookResult.Continue;
        }

        [GameEventHandler]
        public HookResult OnPlayerDisconnect(EventPlayerDisconnect @event, GameEventInfo info)
        {
            var player = @event.Userid;
            if (!IsPlayerValid(player)) return HookResult.Continue;

            PlayerList.Remove(player.Slot);
            LogDebug($"Player disconnected: {player.UserId}");
            return HookResult.Continue;
        }

        private string ConvertToSteam2(long steamId64)
        {
            long steamId3 = steamId64 - SteamId64Base;
            int y = (int)(steamId3 % 2);
            long z = steamId3 / 2;

            return $"STEAM_1:{y}:{z}";
        }

        private int CalculatePlayTime(PlayerInfo playerInfo)
        {
            if (playerInfo == null)
            {
                LogDebug("Player info is null.");
                return 0;
            }

            LogDebug($"Calculating play time for player: {playerInfo.Name}");
            var currentTime = DateTime.Now;
            LogDebug($"Current time: {currentTime}, SessionStartTime: {playerInfo.SessionStartTime}");

            if (currentTime < playerInfo.SessionStartTime)
            {
                LogDebug($"Current time is earlier than the session start time for {playerInfo.Name}.");
                return 0;
            }

            TimeSpan timeSpent = currentTime - playerInfo.SessionStartTime;
            if (timeSpent.TotalSeconds > int.MaxValue)
            {
                LogDebug($"Play time overflow for {playerInfo.Name}.");
                return int.MaxValue;
            }

            int playTimeInSeconds = (int)timeSpent.TotalSeconds;
            LogDebug($"Play time for {playerInfo.Name}: {playTimeInSeconds} seconds");
            return playTimeInSeconds;
        }

        private void LogDebug(string message)
        {
            if (isDebugMode)
            {
                Console.WriteLine($"{DateTime.Now} | {LogPrefix} {message}");
            }
        }

        private async Task UpdatePlayerInfoAsync()
        {
            LogDebug("Updating player info for all players...");

            if (!PlayerList.Any())
            {
                LogDebug("No players on the server.");
                await Task.Run(() => CounterStrikeSharp.API.Server.NextFrame(() => SendServerInfoAsync().Wait()));
                return;
            }

            foreach (var playerSlot in PlayerList.Keys.ToList())
            {
                CCSPlayerController? player = null;
                var resetEvent = new ManualResetEvent(false);

                await Task.Run(() => CounterStrikeSharp.API.Server.NextFrame(() =>
                {
                    player = Utilities.GetPlayerFromSlot(playerSlot);
                    resetEvent.Set();
                }));

                resetEvent.WaitOne();

                if (player == null || !IsPlayerValid(player)) continue;

                var playerInfo = PlayerList[playerSlot];
                await Task.Run(() => CounterStrikeSharp.API.Server.NextFrame(() => UpdatePlayerStats(player, playerInfo)));
                LogPlayerInfo(playerInfo);

                CounterStrikeSharp.API.Server.NextFrame(() => SendServerInfoAsync(playerInfo).Wait());
            }

            LogDebug("All player info updated.");
        }

        private void UpdatePlayerStats(CCSPlayerController player, PlayerInfo playerInfo)
        {
            playerInfo.Name = player.PlayerName;
            playerInfo.Kills = player.ActionTrackingServices?.MatchStats.Kills;
            playerInfo.Deaths = player.ActionTrackingServices?.MatchStats.Deaths;
            playerInfo.Assists = player.ActionTrackingServices?.MatchStats.Assists;
            playerInfo.Headshots = player.ActionTrackingServices?.MatchStats.HeadShotKills;
        }

        private void LogPlayerInfo(PlayerInfo playerInfo)
        {
            LogDebug($"Player info - Name: {playerInfo.Name}, SteamID: {playerInfo.SteamId}, Kills: {playerInfo.Kills}, Deaths: {playerInfo.Deaths}, Assists: {playerInfo.Assists}, Headshots: {playerInfo.Headshots}");
        }

        private static (int ctScore, int terroristScore) GetTeamsScore()
        {
            var teamEntities = Utilities.FindAllEntitiesByDesignerName<CCSTeam>("cs_team_manager");
            var ctScore = teamEntities.FirstOrDefault(t => t.Teamname == TeamCT)?.Score ?? 0;
            var terroristScore = teamEntities.FirstOrDefault(t => t.Teamname == TeamTerrorist)?.Score ?? 0;

            return (ctScore, terroristScore);
        }

        private async Task SendServerInfoAsync(PlayerInfo? playerinfo = null)
        {
            LogDebug("Preparing to send server info...");
            if (string.IsNullOrEmpty(Server) || string.IsNullOrEmpty(Url) || string.IsNullOrEmpty(Password))
            {
                return;
            }

            var jsonPlayers = playerinfo != null ? GetPlayersJson(playerinfo) : new List<object>();
            var (scoreCt, scoreT) = GetTeamsScore();
            var jsonData = new { score_ct = scoreCt, score_t = scoreT, players = jsonPlayers };
            var jsonString = System.Text.Json.JsonSerializer.Serialize(jsonData);

            await PostData(jsonString);
            LogDebug("Server info sent.");
        }

        private List<object> GetPlayersJson(PlayerInfo playerinfo)
        {
            var playTime = CalculatePlayTime(playerinfo);
            int rank = GetRankFromDatabaseAsync(playerinfo.SteamId2).Result;
            var playerJson = new
            {
                name = playerinfo.Name ?? "Unknown",
                steamid = playerinfo?.SteamId,
                kills = playerinfo?.Kills,
                assists = playerinfo?.Assists,
                death = playerinfo?.Deaths,
                headshots = playerinfo?.Headshots,
                rank,
                playtime = playTime
            };

            return new List<object> { playerJson };
        }

        private async Task PostData(string jsonData)
        {
            LogDebug("Sending data to server...");
            var requestUri = $"{Url}/app/modules/module_block_main_monitoring_rating/forward/js_controller.php?server={Server}&password={Password}";

            using var request = new HttpRequestMessage(HttpMethod.Post, requestUri)
            {
                Content = new StringContent(jsonData, Encoding.UTF8, "application/json")
            };

            try
            {
                var response = await httpClient.SendAsync(request);
                var responseContent = await response.Content.ReadAsStringAsync();

                LogDebug($"Response status: {response.StatusCode}. Data sent. Response: {responseContent}");

                if (!response.IsSuccessStatusCode)
                {
                    LogDebug("Error when sending a request: " + responseContent);
                    return;
                }
            }
            catch (Exception ex)
            {
                LogDebug($"Exception in HTTP request: {ex.Message}");
            }
        }

        public static bool IsPlayerValid(CCSPlayerController? player)
        {
            return player is
            {
                Pawn.IsValid: true,
                IsBot: false,
                IsHLTV: false
            };
        }

        [GeneratedRegex("\"([^\"]+)\"\\s+\"([^\"]+)\"")]
        private static partial Regex ConfigLineRegex();
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
        [Required]
        public string? DbHost { get; set; }
        [Required]
        public string? DbUser { get; set; }
        [Required]
        public string? DbPassword { get; set; }
        [Required]
        public string? DbName { get; set; }
        [Required]
        public string? DbPort { get; set; }
        [Required]
        public string? Name { get; set; }
    }

    public class AlternativeConfig
    {
        [Required]
        public string? TableName { get; set; }
        [Required]
        public ConnectionConfig? Connection { get; set; }
    }

    public class ConnectionConfig
    {
        [Required]
        public string? Host { get; set; }
        [Required]
        public string? Database { get; set; }
        [Required]
        public string? User { get; set; }
        [Required]
        public string? Password { get; set; }
    }
}
