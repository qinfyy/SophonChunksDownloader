using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Web;
using NLog;

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

        // 缓存完整分支信息 (游戏ID, 区域) -> BranchesGameBranch
        private static readonly ConcurrentDictionary<(string GameId, string Region), BranchesGameBranch> _分支缓存 = new();
        // 分通道版本缓存 (游戏ID, 区域, 通道) -> 版本号
        private static readonly ConcurrentDictionary<(string GameId, string Region, string Branch), string> _版本缓存 = new();

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
            ["BILIBILIYS"] = (
                "https://hyp-api.mihoyo.com/hyp/hyp-connect/api/getGameBranches",
                "https://downloader-api.mihoyo.com/downloader/sophon_chunk/api/getBuild",
                "umfgRO5gh5",
                "ddxf5qt290cg"
            ),
            ["BILIBILISR"] = (
                "https://hyp-api.mihoyo.com/hyp/hyp-connect/api/getGameBranches",
                "https://downloader-api.mihoyo.com/downloader/sophon_chunk/api/getBuild",
                "6P5gHMNyK3",
                "ddxf5qt290cg"
            ),
            ["BILIBILIJQL"] = (
                "https://hyp-api.mihoyo.com/hyp/hyp-connect/api/getGameBranches",
                "https://downloader-api.mihoyo.com/downloader/sophon_chunk/api/getBuild",
                "xV0f4r1GT0",
                "ddxf5qt290cg"
            )
        };

        private static readonly Dictionary<(string 游戏ID, string 区域), string> _游戏REL映射 = new()
        {
            {("nap", "OSREL"), "U5hbdsT9W7"},
            {("nap", "CNREL"), "x6znKlJ0xK"},
            {("hkrpg", "OSREL"), "4ziysqXOQ8"},
            {("hkrpg", "CNREL"), "64kMb5iAWu"},
            {("hk4e", "OSREL"), "gopR6Cufr3"},
            {("hk4e", "CNREL"), "1Z8W5NHUQb"},
            {("bh3", "CNREL"), "osvnlOc0S8"},
            {("bh3_global", "OSREL"), "5TIVvvcwtM"},
            {("bh3_jp", "OSREL"), "g0mMIvshDb"},
            {("bh3_kr", "OSREL"), "uxB4MC7nzC"},
            {("bh3_tw", "OSREL"), "wkE5P5WsIf"},
            {("bh3_sea", "OSREL"), "bxPTXSET5t"},
            {("nap", "BILIBILIJQL"), "HXAFlmYa17"},
            {("hkrpg", "BILIBILISR"), "EdtUqXfCHh"},
            {("hk4e", "BILIBILIYS"), "T2S0Gz4Dr2"},
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
                new GameInfo { DisplayName = "崩坏3 (欧美服)", GameId = "bh3_global", Region = "OSREL" },
                new GameInfo { DisplayName = "崩坏3 (日服)", GameId = "bh3_jp", Region = "OSREL" },
                new GameInfo { DisplayName = "崩坏3 (韩服)", GameId = "bh3_kr", Region = "OSREL" },
                new GameInfo { DisplayName = "崩坏3 (繁中服)", GameId = "bh3_tw", Region = "OSREL" },
                new GameInfo { DisplayName = "崩坏3 (东南亚服)", GameId = "bh3_sea", Region = "OSREL" },
                new GameInfo { DisplayName = "原神 (B服)", GameId = "hk4e", Region = "BILIBILIYS" },
                new GameInfo { DisplayName = "崩坏：星穹铁道 (B服)", GameId = "hkrpg", Region = "BILIBILISR" },
                new GameInfo { DisplayName = "绝区零 (B服)", GameId = "nap", Region = "BILIBILIJQL" },
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
            var relId = 获取REL游戏ID(游戏ID, 区域);

            var uri = new UriBuilder(apiBase);
            var query = HttpUtility.ParseQueryString(uri.Query);
            query["game_ids[]"] = relId;
            query["launcher_id"] = launcherId;
            uri.Query = query.ToString();

            logger.Debug($"请求: {uri}");
            string json = await _hc.GetStringAsync(uri.ToString());
            return JsonSerializer.Deserialize<BranchesRoot>(json);
        }

        public static async Task<BranchesGameBranch> 获取游戏分支(string 游戏ID, string 区域)
        {
            var 缓存键 = (游戏ID, 区域);
            if (_分支缓存.TryGetValue(缓存键, out var 缓存数据))
                return 缓存数据;

            var 分支数据 = await 获取分支数据(游戏ID, 区域);
            if (分支数据.retcode != 0)
                throw new InvalidOperationException($"获取分支信息失败: {分支数据.message}");

            var relId = 获取REL游戏ID(游戏ID, 区域);
            var gameBranch = 分支数据.data.game_branches
                .FirstOrDefault(b => b.game.id == relId);

            if (gameBranch == null)
                throw new InvalidOperationException($"未找到游戏 {游戏ID} 在区域 {区域} 的分支信息");

            _分支缓存[缓存键] = gameBranch;
            return gameBranch;
        }

        public static BranchesGameBranch? 获取缓存的游戏分支(string 游戏ID, string 区域)
        {
            var 缓存键 = (游戏ID, 区域);
            return _分支缓存.TryGetValue(缓存键, out var 分支) ? 分支 : null;
        }

        public static string 构建GetBuild地址(
            string 游戏ID,
            string 区域,
            string packageId,
            string password,
            string? 版本号,
            string branch = "main")
        {
            if (!_区域配置.TryGetValue(区域, out var 配置))
                throw new ArgumentException($"不支持的区域: {区域}");

            var (_, sophonBase, _, platApp) = 配置;

            var uri = new UriBuilder(sophonBase);
            var query = HttpUtility.ParseQueryString(uri.Query);
            query["branch"] = branch;
            query["package_id"] = packageId;
            query["password"] = password;
            query["plat_app"] = platApp;

            // mhy api特殊处理：predownload通道请求最新版本时，不传tag参数
            if (branch != "predownload" || !string.IsNullOrEmpty(版本号))
            {
                query["tag"] = 版本号 ?? "";
            }

            uri.Query = query.ToString();
            return uri.ToString();
        }

        public static string 获取缓存的最新版本(string 游戏ID, string 区域, string branch = "main")
        {
            var key = (游戏ID, 区域, branch);
            return _版本缓存.TryGetValue(key, out var 版本) ? 版本 : "";
        }

        public static async Task 预加载最新版本()
        {
            var 所有游戏 = 获取支持的游戏列表();
            var 游戏分组 = 所有游戏
                .GroupBy(g => g.Region)
                .ToDictionary(g => g.Key, g => g.ToList());

            var 任务列表 = new List<Task>();

            foreach (var (区域, 游戏列表) in 游戏分组)
            {
                if (!_区域配置.TryGetValue(区域, out var 配置))
                {
                    logger.Warn($"跳过不支持的区域: {区域}");
                    continue;
                }

                var (apiBase, _, launcherId, _) = 配置;

                var relIds = 游戏列表.Select(g => 获取REL游戏ID(g.GameId, 区域)).ToList();
                // 构建参数
                var sb = new StringBuilder();
                for (int i = 0; i < relIds.Count; i++)
                {
                    if (i > 0)
                    {
                        sb.Append('&');
                    }

                    sb.Append("game_ids%5B%5D="); // %5B%5D = []
                    sb.Append(Uri.EscapeDataString(relIds[i]));
                }
                sb.Append('&').Append("launcher_id=").Append(Uri.EscapeDataString(launcherId));
                string 完整Url = $"{apiBase.TrimEnd(' ', '\t')}?{sb.ToString()}";

                var 任务 = Task.Run(async () =>
                {
                    try
                    {
                        logger.Debug($"请求: {完整Url}");
                        string json = await _hc.GetStringAsync(完整Url);
                        var 响应 = JsonSerializer.Deserialize<BranchesRoot>(json);

                        if (响应.retcode != 0)
                        {
                            logger.Warn($"批量请求失败 ({区域}): {响应.message}");
                            return;
                        }

                        // game.id -> BranchesGameBranch 的映射
                        var 分支映射 = 响应.data.game_branches
                            .ToDictionary(b => b.game.id, b => b);

                        // 为每个游戏写入缓存
                        foreach (var 游戏 in 游戏列表)
                        {
                            string relId = 获取REL游戏ID(游戏.GameId, 区域);
                            if (分支映射.TryGetValue(relId, out var 分支))
                            {
                                // 缓存完整分支信息
                                _分支缓存[(游戏.GameId, 游戏.Region)] = 分支;

                                // 缓存主通道版本
                                if (分支.main?.tag != null)
                                {
                                    _版本缓存[(游戏.GameId, 游戏.Region, "main")] = 分支.main.tag;
                                    logger.Debug($"已获取 {游戏.DisplayName} (main) 最新版本: {分支.main.tag}");
                                }
                                else
                                {
                                    logger.Warn($"未找到 {游戏.DisplayName} 的 main 分支版本");
                                }

                                // 缓存预下载通道版本
                                if (分支.pre_download?.tag != null)
                                {
                                    _版本缓存[(游戏.GameId, 游戏.Region, "predownload")] = 分支.pre_download.tag;
                                    logger.Debug($"已获取 {游戏.DisplayName} (predownload) 最新版本: {分支.pre_download.tag}");
                                }
                            }
                            else
                            {
                                logger.Warn($"未找到 {游戏.DisplayName} 的分支信息");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.Warn(ex, $"获取最新版本 {区域} 失败");
                    }
                });

                任务列表.Add(任务);
            }

            await Task.WhenAll(任务列表);
        }
    }
}
