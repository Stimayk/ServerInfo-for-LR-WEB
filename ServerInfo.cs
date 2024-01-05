using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API;
using System.Text.RegularExpressions;
using System.Text;
using CounterStrikeSharp.API.Modules.Timers;

public class ServerInfo : BasePlugin
{
    public override string ModuleName => "ServerInfo for LR WEB";
    public override string ModuleVersion => "1.0";
    private const int MaxPlayers = 65;
    private string server = "";
    private string password = "";
    private string url = "";
    private float timerInterval = 40.0f;

    public override void Load(bool hotReload)
    {
        LoadCfg();
        var ip = GetIP();
        AddCommand("css_getserverinfo", "Get server info", (player, info) => OnGetInfo());
        AddTimer(timerInterval, OnGetInfo, TimerFlags.REPEAT);
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
                if (line.Contains("}"))
                {
                    insideServerBlock = false;
                    continue;
                }

                var match = Regex.Match(line, "\"([^\"]+)\"\\s+\"([^\"]+)\"");
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

    private string GetIP()
    {
        var serverIp = ConVar.Find("ip")?.StringValue ?? "Unknown IP";
        var serverPort = ConVar.Find("hostport")?.GetPrimitiveValue<int>().ToString() ?? "Unknown Port";
        return $"{serverIp}:{serverPort}";
    }

    private void OnGetInfo()
    {
        if (!string.IsNullOrEmpty(server))
        {
            SendServerInfo();
        }
        else
        {
            Server.PrintToConsole("Ошибка: информация о сервере не установлена.");
        }
    }

    private (int t1score, int t2score) GetTeamsScore()
    {
        var teamEntities = Utilities.FindAllEntitiesByDesignerName<CCSTeam>("cs_team_manager");
        var t1score = teamEntities.FirstOrDefault(t => t.Teamname == "CT")?.Score ?? 0;
        var t2score = teamEntities.FirstOrDefault(t => t.Teamname == "TERRORIST")?.Score ?? 0;
        return (t1score, t2score);
    }

    private void SendServerInfo()
    {
        if (string.IsNullOrEmpty(server) || string.IsNullOrEmpty(url) || string.IsNullOrEmpty(password))
        {
            Server.PrintToConsole("URL, сервер или пароль не установлены.");
            return;
        }

        var jsonPlayers = GetPlayersJson();
        var (t1score, t2score) = GetTeamsScore();
        var jsonData = new { score_ct = t1score, score_t = t2score, players = jsonPlayers };
        var jsonString = System.Text.Json.JsonSerializer.Serialize(jsonData);

        PostData(jsonString);
    }

    private List<object> GetPlayersJson()
    {
        var jsonPlayers = new List<object>();
        for (int i = 0; i < MaxPlayers; i++)
        {
            var player = Utilities.GetPlayerFromIndex(i);
            if (player == null || !player.IsValid || player.IsBot || player.SteamID == 0 || player.PlayerPawn == null) continue;
            if (player.ActionTrackingServices == null || player.ActionTrackingServices.MatchStats == null) continue;

            jsonPlayers.Add(new
            {
                name = player.PlayerName,
                steamid = player.SteamID,
                kills = player.ActionTrackingServices.MatchStats.Kills,
                assists = player.ActionTrackingServices.MatchStats.Assists,
                death = player.ActionTrackingServices.MatchStats.Deaths,
                headshots = player.ActionTrackingServices.MatchStats.HeadShotKills,
                rank = "0",
                playtime = 0
            });
        }

        return jsonPlayers;
    }

    private void PostData(string jsonData)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"{url}/app/modules/module_block_main_monitoring_rating/forward/js_controller.php?server={server}&password={password}")
        {
            Content = new StringContent(jsonData, Encoding.UTF8, "application/json")
        };

        using (var httpClient = new HttpClient())
        {
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
    }
}