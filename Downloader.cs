using System.Security.Cryptography;
using ZstdSharp;
using NLog;

namespace SophonChunksDownloader
{
    public class DownloadProgress
    {
        public int 总文件数 { get; set; }
        public int 已完成文件数 { get; set; }
        public long 总字节数 { get; set; }
        public long 已下载字节数 { get; set; }
        public string 当前速度 { get; set; } = "0 KB/s";
        public string 状态文本 { get; set; } = "";
    }

    public class Downloader
    {
        private static readonly HttpClient _hc = new HttpClient();
        private const int 最大重试次数 = 3;

        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private SemaphoreSlim? _并发信号量;
        private readonly ManualResetEventSlim _暂停事件 = new ManualResetEventSlim(true);
        private readonly object _暂停锁 = new object();
        private bool _暂停 = false;

        // 回调委托
        public Action<DownloadProgress>? 进度更新回调 { get; set; }
        public Action<string>? 状态文本回调 { get; set; }
        public Action? 下载完成回调 { get; set; }
        public Action? 下载取消回调 { get; set; }

        private int _总文件数 = 0;
        private int _已完成文件数 = 0;
        private long _总字节数 = 0;
        private long _已下载字节数 = 0;

        private long _上次更新字节数 = 0;
        private long _上次更新时间 = 0;
        private string _当前速度 = "0 KB/s";
        private double _平滑速度字节每秒 = 0;
        private const double _平滑系数 = 0.1;

        public void 暂停或继续()
        {
            lock (_暂停锁)
            {
                if (!_暂停)
                {
                    _暂停 = true;
                    _暂停事件.Reset();
                    logger.Info("下载已暂停");
                }
                else
                {
                    _暂停 = false;
                    _暂停事件.Set();
                    logger.Info("下载已继续");
                }
            }
        }

        public bool 是否暂停 => _暂停;

        public async Task 开始下载(
            List<SophonChunkFile> 所有文件列表,
            Dictionary<string, string> 文件清单字典,
            string 保存目录,
            int 最大并发 = 16,
            bool 是否清理多余文件 = false)
        {
            _cts.Token.ThrowIfCancellationRequested();

            _总文件数 = 所有文件列表.Count;
            if (_总文件数 == 0)
            {
                var msg = "没有找到任何需要下载的文件";
                状态文本回调?.Invoke(msg);
                logger.Warn(msg);
                return;
            }

            _总字节数 = 所有文件列表.Sum(a => a.Size);
            _已完成文件数 = 0;
            _已下载字节数 = 0;
            _上次更新字节数 = 0;
            _上次更新时间 = Environment.TickCount;
            _当前速度 = "0 KB/s";

            var infoStr = "文件总数：{0} ，共 {1}";
            logger.Info(infoStr, _总文件数, _总字节数);
            状态文本回调?.Invoke(string.Format(infoStr, _总文件数, 实用工具.格式化文件大小(_总字节数)));
            
            _并发信号量 = new SemaphoreSlim(最大并发, 最大并发);
            var 下载任务 = new List<Task>();

            try
            {
                foreach (var 文件 in 所有文件列表)
                {
                    if (_cts.IsCancellationRequested) break;
                    await _并发信号量.WaitAsync(_cts.Token);

                    下载任务.Add(Task.Run(async () =>
                    {
                        try
                        {
                            await 下载文件_异步(文件, 文件清单字典[文件.File], 保存目录, _cts.Token);
                        }
                        catch (OperationCanceledException)
                        {
                            logger.Debug($"文件 {文件.File} 下载被取消");
                        }
                        catch (Exception ex)
                        {
                            logger.Error(ex, $"文件 {文件.File} 下载失败: {ex.Message}");
                        }
                        finally
                        {
                            _并发信号量!.Release();
                        }
                    }, _cts.Token));
                }

                await Task.WhenAll(下载任务);

                if (!_cts.IsCancellationRequested)
                {
                    if (是否清理多余文件)
                    {
                        await 清理多余文件(所有文件列表, 保存目录);
                    }

                    logger.Info("所有文件下载完成");
                    下载完成回调?.Invoke();
                }
                else
                {
                    logger.Info("下载被用户取消");
                    下载取消回调?.Invoke();
                }
            }
            catch (OperationCanceledException)
            {
                logger.Info("下载被取消（OperationCanceledException）");
                下载取消回调?.Invoke();
            }
            catch (Exception ex)
            {
                logger.Fatal(ex, "下载过程中发生未预期异常");
                throw;
            }
        }

        public void 取消下载()
        {
            logger.Info("收到取消下载请求");
            _cts.Cancel();
        }

        public void 重置状态()
        {
            _总文件数 = 0;
            _已完成文件数 = 0;
            _总字节数 = 0;
            _已下载字节数 = 0;
            _上次更新字节数 = 0;
            _当前速度 = "0 KB/s";
            logger.Debug("下载状态已重置");
        }

        private async Task 下载文件_异步(SophonChunkFile file, string 分块路径前缀, string 保存路径, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            _暂停事件.Wait(ct);

            var filePath = Path.Combine(保存路径, file.File);
            var tmpPath = filePath + ".tmp";

            if (File.Exists(tmpPath))
            {
                logger.Debug($"删除残留临时文件: {tmpPath}");
                File.Delete(tmpPath);
            }

            var dir = Path.GetDirectoryName(filePath)!;
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
                logger.Debug($"创建目录: {dir}");
            }

            // 检查是否已存在且 MD5 匹配
            if (File.Exists(filePath))
            {
                using (var fs = File.OpenRead(filePath))
                {
                    var 存在文件Md5 = await 实用工具.计算Md5_异步(fs);
                    if (存在文件Md5.Equals(file.Md5, StringComparison.OrdinalIgnoreCase))
                    {
                        logger.Info($"文件已存在且校验通过，跳过: {filePath}");
                        Interlocked.Add(ref _已下载字节数, file.Size);
                        Interlocked.Increment(ref _已完成文件数);
                        更新进度();
                        return;
                    }
                    else
                    {
                        logger.Warn($"文件存在但 MD5 不匹配，将重新下载: {filePath}");
                    }
                }
            }

            logger.Info($"开始下载文件: {filePath} (大小: {file.Size})");

            try
            {
                using (var fs = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, FileOptions.Asynchronous))
                using (var md5 = IncrementalHash.CreateHash(HashAlgorithmName.MD5))
                {
                    foreach (var chunk in file.Chunks)
                    {
                        ct.ThrowIfCancellationRequested();
                        _暂停事件.Wait(ct);
                        await 处理分块_异步(chunk, 分块路径前缀, fs, md5, ct);
                    }

                    var 计算Md5 = BitConverter.ToString(md5.GetHashAndReset()).Replace("-", "").ToLower();

                    if (!计算Md5.Equals(file.Md5, StringComparison.OrdinalIgnoreCase))
                    {
                        var errMsg = $"文件MD5校验失败: {filePath}\n计算Md5: {计算Md5}\n正确MD5: {file.Md5}";
                        logger.Error(errMsg);
                        throw new Exception(errMsg);
                    }

                    logger.Debug($"文件 MD5 校验通过: {filePath}");
                }

                File.Move(tmpPath, filePath, true);
                logger.Info($"下载完成: {filePath}");
            }
            catch
            {
                if (File.Exists(tmpPath))
                {
                    logger.Debug($"清理失败的临时文件: {tmpPath}");
                    File.Delete(tmpPath);
                }
                throw;
            }
            finally
            {
                Interlocked.Increment(ref _已完成文件数);
                更新进度();
            }
        }

        private void 更新进度()
        {
            long 当前时间 = Environment.TickCount;
            long 时间差 = 当前时间 - _上次更新时间;

            if (时间差 > 100)
            {
                long 字节增量 = _已下载字节数 - _上次更新字节数;
                double 实时速度字节每秒 = (字节增量 * 1000.0) / 时间差;

                if (_平滑速度字节每秒 == 0)
                    _平滑速度字节每秒 = 实时速度字节每秒;
                else
                    _平滑速度字节每秒 = _平滑系数 * 实时速度字节每秒 + (1 - _平滑系数) * _平滑速度字节每秒;

                _当前速度 = 实用工具.格式化速度(实时速度字节每秒);

                _上次更新字节数 = _已下载字节数;
                _上次更新时间 = 当前时间;
            }

            long 剩余字节数 = _总字节数 - _已下载字节数;
            string 剩余时间文本 = "剩余: ";

            if (剩余字节数 > 0)
            {
                double 秒数 = 剩余字节数 / _平滑速度字节每秒;
                剩余时间文本 += 实用工具.格式化剩余时间(秒数);
            }

            var progress = new DownloadProgress
            {
                总文件数 = _总文件数,
                已完成文件数 = _已完成文件数,
                总字节数 = _总字节数,
                已下载字节数 = _已下载字节数,
                当前速度 = _当前速度,
                状态文本 = $"已完成: {_已完成文件数}/{_总文件数}, " +
                           $"{实用工具.格式化文件大小(_已下载字节数)}/{实用工具.格式化文件大小(_总字节数)} " +
                           $"[速度: {_当前速度}] [{剩余时间文本}]"
            };

            进度更新回调?.Invoke(progress);
        }

        private async Task 处理分块_异步(
            SophonChunk chunk,
            string 分块路径前缀,
            FileStream 输出流,
            IncrementalHash md5,
            CancellationToken ct)
        {
            var url = 分块路径前缀 + chunk.Id;
            logger.Debug($"开始下载分块: {chunk.Id} (URL: {url})");

            Exception? 最后异常 = null;

            using (var 压缩数据 = new MemoryStream())
            {
                for (int i = 0; i < 最大重试次数; i++)
                {
                    try
                    {
                        压缩数据.SetLength(0);
                        using (var response = await _hc.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct))
                        {
                            response.EnsureSuccessStatusCode();
                            await response.Content.CopyToAsync(压缩数据, ct);
                            logger.Debug($"分块 {chunk.Id} 下载完成，大小: {压缩数据.Length}");
                            break;
                        }
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        最后异常 = ex;
                        logger.Warn(ex, $"分块 {chunk.Id} 下载失败（第 {i + 1} 次重试）: {ex.Message}");
                        if (i < 最大重试次数 - 1)
                        {
                            var delayMs = (int)Math.Pow(2, i) * 1000;
                            logger.Debug($"等待 {delayMs}ms 后重试分块 {chunk.Id}");
                            await Task.Delay(delayMs, ct);
                        }
                    }
                }

                if (压缩数据.Length == 0)
                {
                    var errStr = $"分块 {chunk.Id} 下载失败（重试{最大重试次数}次）: {url}\n原因: {最后异常?.Message}";
                    logger.Error(errStr);
                    throw new Exception(errStr);
                }

                if (压缩数据.Length != chunk.CompressedSize)
                {
                    var errStr = $"分块 {chunk.Id} 压缩数据大小不匹配: 期望 {chunk.CompressedSize}, 实际 {压缩数据.Length}";
                    logger.Error(errStr);
                    throw new Exception(errStr);
                }

                压缩数据.Seek(0, SeekOrigin.Begin);
                var 压缩数据Md5 = await 实用工具.计算Md5_异步(压缩数据);
                if (!压缩数据Md5.Equals(chunk.CompressedMd5, StringComparison.OrdinalIgnoreCase))
                {
                    var errStr = $"分块 {chunk.Id} 压缩数据 MD5 校验失败: 计算 {压缩数据Md5}, 期望 {chunk.CompressedMd5}";
                    logger.Error(errStr);
                    throw new Exception(errStr);
                }
                压缩数据.Seek(0, SeekOrigin.Begin);

                logger.Debug($"分块 {chunk.Id} 压缩数据校验通过，开始解压");

                using (var 解压数据 = new MemoryStream())
                {
                    await 解压分块_异步(chunk, 压缩数据, 解压数据, md5, ct);

                    long 分块实际大小 = chunk.UncompressedSize;
                    Interlocked.Add(ref _已下载字节数, 分块实际大小);
                    更新进度();

                    解压数据.Seek(0, SeekOrigin.Begin);
                    await 解压数据.CopyToAsync(输出流, ct);
                }

                logger.Debug($"分块 {chunk.Id} 处理完成，解压后大小: {chunk.UncompressedSize}");
            }
        }

        private static async Task 解压分块_异步(
            SophonChunk chunk,
            Stream 压缩数据流,
            Stream 输出流,
            IncrementalHash md5,
            CancellationToken ct)
        {
            using (var 解压流 = new DecompressionStream(压缩数据流))
            {
                const int 缓冲区大小 = 524288;
                var 缓冲区 = new byte[缓冲区大小];
                long pos = 0;
                using (var 原始Md5 = MD5.Create())
                {
                    while (true)
                    {
                        ct.ThrowIfCancellationRequested();
                        var 读入字节数 = await 解压流.ReadAsync(缓冲区, 0, 缓冲区.Length, ct);
                        if (读入字节数 == 0) break;

                        await 输出流.WriteAsync(缓冲区, 0, 读入字节数, ct);
                        原始Md5.TransformBlock(缓冲区, 0, 读入字节数, null, 0);
                        md5.AppendData(缓冲区, 0, 读入字节数);
                        pos += 读入字节数;
                    }

                    原始Md5.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                    var 计算原始Md5 = BitConverter.ToString(原始Md5.Hash!).Replace("-", "").ToLower();

                    if (pos != chunk.UncompressedSize)
                    {
                        throw new Exception($"分块 {chunk.Id} 解压后大小不匹配: 期望 {chunk.UncompressedSize}, 实际 {pos}");
                    }

                    if (!计算原始Md5.Equals(chunk.UncompressedMd5, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new Exception($"分块 {chunk.Id} 解压后 MD5 校验失败: 计算 {计算原始Md5}, 期望 {chunk.UncompressedMd5}");
                    }
                }
            }
        }

        private async Task 清理多余文件(List<SophonChunkFile> 所有文件列表, string 保存目录)
        {
            try
            {
                var 预期文件路径 = new HashSet<string>(
                    所有文件列表.Select(f => Path.GetFullPath(Path.Combine(保存目录, f.File))),
                    StringComparer.OrdinalIgnoreCase
                );

                var 实际文件路径 = Directory.GetFiles(保存目录, "*", SearchOption.AllDirectories)
                                             .Select(Path.GetFullPath)
                                             .ToList();

                var 多余文件 = 实际文件路径.Where(f => !预期文件路径.Contains(f)).ToList();

                if (多余文件.Count > 0)
                {
                    logger.Debug($"发现 {多余文件.Count} 个多余文件，正在清理...");

                    foreach (var filePath in 多余文件)
                    {
                        try
                        {
                            File.Delete(filePath);
                            logger.Debug($"已删除多余文件: {filePath}");
                        }
                        catch (Exception ex)
                        {
                            logger.Warn(ex, $"无法删除文件: {filePath}，原因: {ex.Message}");
                        }
                    }
                }

                if (Directory.Exists(保存目录))
                {
                    var 所有目录 = Directory.GetDirectories(保存目录, "*", SearchOption.AllDirectories)
                                            .OrderByDescending(d => d.Length)
                                            .ToList();

                    foreach (var dir in 所有目录)
                    {
                        if (Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any())
                        {
                            try
                            {
                                Directory.Delete(dir);
                                logger.Debug($"已删除目录: {dir}");
                            }
                            catch (Exception ex)
                            {
                                logger.Warn(ex, $"无法删除目录: {dir}: {ex.Message}");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, $"清理文件时发生错误: {ex.Message}");
            }
        }

        public void Dispose()
        {
            _cts.Cancel();
            _cts.Dispose();
            _暂停事件.Dispose();
            LogManager.Flush();
        }
    }
}
