using NLog;
using System.Text.Json;
using System.Web;

namespace SophonChunksDownloader
{
    public class GameInfo
    {
        public string DisplayName { get; set; }
        public string GameId { get; set; }
        public string Region { get; set; }

        public override string ToString() => DisplayName;
    }

    public static class GameUrlBuilder
    {
        private static readonly HttpClient _hc = new HttpClient();
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

        private static readonly Dictionary<string, (string ApiBase, string SophonBase, string LauncherId, string PlatApp)> _区域配置 = new()
        {
            ["CNREL"] = (
                "https://hyp-api.mihoyo.com/hyp/hyp-connect/api/getGameBranches",
                "https://api-takumi.mihoyo.com/downloader/sophon_chunk/api/getBuild",
                "jGHBHlcOq1",
                "ddxf5qt290cg"
            ),
            ["OSREL"] = (
                "https://sg-hyp-api.hoyoverse.com/hyp/hyp-connect/api/getGameBranches",
                "https://sg-public-api.hoyoverse.com/downloader/sophon_chunk/api/getBuild",
                "VYTpXlbWo8",
                "ddxf6vlr1reo"
            ),
            ["JP"] = (
                "https://sg-hyp-api.hoyoverse.com/hyp/hyp-connect/api/getGameBranches",
                "https://sg-public-api.hoyoverse.com/downloader/sophon_chunk/api/getBuild",
                "VYTpXlbWo8",
                "ddxf6vlr1reo"
            ),
            ["KR"] = (
                "https://sg-hyp-api.hoyoverse.com/hyp/hyp-connect/api/getGameBranches",
                "https://sg-public-api.hoyoverse.com/downloader/sophon_chunk/api/getBuild",
                "VYTpXlbWo8",
                "ddxf6vlr1reo"
            ),
            ["TW"] = (
                "https://sg-hyp-api.hoyoverse.com/hyp/hyp-connect/api/getGameBranches",
                "https://sg-public-api.hoyoverse.com/downloader/sophon_chunk/api/getBuild",
                "VYTpXlbWo8",
                "ddxf6vlr1reo"
            ),
            ["SEA"] = (
                "https://sg-hyp-api.hoyoverse.com/hyp/hyp-connect/api/getGameBranches",
                "https://sg-public-api.hoyoverse.com/downloader/sophon_chunk/api/getBuild",
                "VYTpXlbWo8",
                "ddxf6vlr1reo"
            )
        };

        // REL ID 映射表
        private static readonly Dictionary<(string 游戏ID, string 区域), string> _游戏REL映射 = new()
        {
            {("nap", "OSREL"), "U5hbdsT9W7"},
            {("nap", "CNREL"), "x6znKlJ0xK"},
            {("hkrpg", "OSREL"), "4ziysqXOQ8"},
            {("hkrpg", "CNREL"), "64kMb5iAWu"},
            {("hk4e", "OSREL"), "gopR6Cufr3"},
            {("hk4e", "CNREL"), "1Z8W5NHUQb"},
            {("bh3", "CNREL"), "osvnlOc0S8"},
            {("bh3", "OSREL"), "5TIVvvcwtM"},
            {("bh3", "JP"), "g0mMIvshDb"},
            {("bh3", "KR"), "uxB4MC7nzC"},
            {("bh3", "TW"), "wkE5P5WsIf"},
            {("bh3", "SEA"), "bxPTXSET5t"},
        };

        public static List<GameInfo> 获取支持的游戏列表()
        {
            return new List<GameInfo>
            {
                new GameInfo { DisplayName = "原神 (国际服)", GameId = "hk4e", Region = "OSREL" },
                new GameInfo { DisplayName = "原神 (国服)", GameId = "hk4e", Region = "CNREL" },
                new GameInfo { DisplayName = "崩坏：星穹铁道 (国际服)", GameId = "hkrpg", Region = "OSREL" },
                new GameInfo { DisplayName = "崩坏：星穹铁道 (国服)", GameId = "hkrpg", Region = "CNREL" },
                new GameInfo { DisplayName = "绝区零 (国际服)", GameId = "nap", Region = "OSREL" },
                new GameInfo { DisplayName = "绝区零 (国服)", GameId = "nap", Region = "CNREL" },
                new GameInfo { DisplayName = "崩坏3 (国服)", GameId = "bh3", Region = "CNREL" },
                new GameInfo { DisplayName = "崩坏3 (欧美服)", GameId = "bh3", Region = "OSREL" },
                new GameInfo { DisplayName = "崩坏3 (日服)", GameId = "bh3", Region = "JP" },
                new GameInfo { DisplayName = "崩坏3 (韩服)", GameId = "bh3", Region = "KR" },
                new GameInfo { DisplayName = "崩坏3 (繁中服)", GameId = "bh3", Region = "TW" },
                new GameInfo { DisplayName = "崩坏3 (东南亚服)", GameId = "bh3", Region = "SEA" },
            };
        }

        private static string 获取REL游戏ID(string 游戏ID, string 区域)
        {
            return _游戏REL映射.TryGetValue((游戏ID, 区域), out var relId) ? relId : 游戏ID;
        }

        private static async Task<BranchesRoot> 获取分支数据(string 游戏ID, string 区域)
        {
            if (!_区域配置.TryGetValue(区域, out var 配置))
                throw new ArgumentException($"不支持的区域: {区域}");

            var (apiBase, _, launcherId, _) = 配置;

            var uri = new UriBuilder(apiBase);
            var query = HttpUtility.ParseQueryString(uri.Query);
            query["game_ids[]"] = 获取REL游戏ID(游戏ID, 区域);
            query["launcher_id"] = launcherId;
            uri.Query = query.ToString();

            logger.Debug($"请求: {uri}");
            string json = await _hc.GetStringAsync(uri.ToString());
            return JsonSerializer.Deserialize<BranchesRoot>(json);
        }

        public static async Task<string> 构建GetBuild地址(string 游戏ID, string 区域, string 版本号)
        {
            var 分支数据 = await 获取分支数据(游戏ID, 区域);
            if (分支数据.retcode != 0)
                throw new InvalidOperationException($"获取分支信息失败: {分支数据.message}");

            var mainBranche = 分支数据.data.game_branches[0].main;
            if (mainBranche == null)
                throw new InvalidOperationException("未找到 main 分支信息");

            if (!_区域配置.TryGetValue(区域, out var 配置))
                throw new ArgumentException($"不支持的区域: {区域}");

            var (_, sophonBase, _, platApp) = 配置;

            var uri = new UriBuilder(sophonBase);
            var query = HttpUtility.ParseQueryString(uri.Query);
            query["branch"] = "main";
            query["package_id"] = mainBranche.package_id;
            query["password"] = mainBranche.password;
            query["plat_app"] = platApp;
            query["tag"] = 版本号;

            uri.Query = query.ToString();
            return uri.ToString();
        }
    }
}
