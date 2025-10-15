using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using ZstdSharp;

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
        private static readonly string _错误文件路径 = "错误.txt";
        private const int 最大重试次数 = 3;

        private readonly BlockingCollection<string> _日志队列 = new BlockingCollection<string>();
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

        public Downloader()
        {
            Task.Run(写入错误);
        }

        public void 暂停或继续()
        {
            lock (_暂停锁)
            {
                if (!_暂停)
                {
                    _暂停 = true;
                    _暂停事件.Reset();
                }
                else
                {
                    _暂停 = false;
                    _暂停事件.Set();
                }
            }
        }

        public bool 是否暂停 => _暂停;

        public async Task 开始下载(
            List<SophonChunkFile> 所有文件列表,
            Dictionary<string, string> 文件清单字典,
            string 保存目录,
            int 最大并发 = 16)
        {
            _cts.Token.ThrowIfCancellationRequested();

            _总文件数 = 所有文件列表.Count;
            if (_总文件数 == 0)
            {
                状态文本回调?.Invoke("没有找到任何需要下载的文件");
                return;
            }

            _总字节数 = 所有文件列表.Sum(a => a.Size);
            _已完成文件数 = 0;
            _已下载字节数 = 0;
            _上次更新字节数 = 0;
            _上次更新时间 = Environment.TickCount;
            _当前速度 = "0 KB/s";

            状态文本回调?.Invoke($"文件总数：{_总文件数} ，共 {实用工具.格式化文件大小(_总字节数)}");

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
                            // 取消，不记录错误
                        }
                        catch (Exception ex)
                        {
                            记录错误($"文件 {文件.File} 下载失败: {ex.Message}");
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
                    下载完成回调?.Invoke();
                }
                else
                {
                    下载取消Callback();
                }
            }
            catch (OperationCanceledException)
            {
                下载取消Callback();
            }
            catch (Exception ex)
            {
                记录错误($"下载失败: {ex.Message}");
                throw;
            }
        }

        private void 下载取消Callback()
        {
            下载取消回调?.Invoke();
        }

        public void 取消下载()
        {
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
        }

        private async Task 下载文件_异步(SophonChunkFile file, string 分块路径前缀, string 保存路径, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            _暂停事件.Wait(ct);

            var filePath = Path.Combine(保存路径, file.File);
            var tmpPath = filePath + ".tmp";
            if (File.Exists(tmpPath))
                File.Delete(tmpPath);

            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);

            if (File.Exists(filePath))
            {
                using (var fs = File.OpenRead(filePath))
                {
                    var 存在文件Md5 = await 实用工具.计算Md5_异步(fs);
                    if (存在文件Md5.Equals(file.Md5, StringComparison.OrdinalIgnoreCase))
                    {
                        Interlocked.Add(ref _已下载字节数, file.Size);
                        var 新文件数 = Interlocked.Increment(ref _已完成文件数);
                        更新进度();
                        return;
                    }
                }
            }

            try
            {
                using (var fs = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, FileOptions.Asynchronous))
                using (var md5 = IncrementalHash.CreateHash(HashAlgorithmName.MD5))
                {
                    foreach (var chunk in file.Chunks)
                    {
                        ct.ThrowIfCancellationRequested();
                        _暂停事件.Wait(ct); // 暂停
                        await 处理分块_异步(chunk, 分块路径前缀, fs, md5, ct);
                    }

                    var 计算Md5 = BitConverter.ToString(md5.GetHashAndReset()).Replace("-", "").ToLower();

                    if (!计算Md5.Equals(file.Md5, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new Exception($"文件MD5校验失败: {filePath}\n计算Md5: {计算Md5}\n正确MD5: {file.Md5}");
                    }
                }

                File.Move(tmpPath, filePath, true);
            }
            catch
            {
                if (File.Exists(tmpPath))
                    File.Delete(tmpPath);
                throw;
            }
            finally
            {
                var 新文件数 = Interlocked.Increment(ref _已完成文件数);
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
                double 速度 = (字节增量 * 1000.0) / 时间差; // 字节/秒
                _当前速度 = 实用工具.格式化速度(速度);

                _上次更新字节数 = _已下载字节数;
                _上次更新时间 = 当前时间;
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
                            $"[速度: {_当前速度}]"
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
            Exception 异常 = null;

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
                            break;
                        }
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        异常 = ex;
                        if (i < 最大重试次数 - 1)
                        {
                            await Task.Delay((int)Math.Pow(2, i) * 1000, ct);
                        }
                    }
                }

                if (压缩数据.Length == 0)
                {
                    throw new Exception($"下载失败(重试{最大重试次数}次): {url}\n原因: {异常?.Message}");
                }

                if (压缩数据.Length != chunk.CompressedSize)
                {
                    throw new Exception($"压缩数据大小不匹配: {chunk.Id}\n期望大小: {chunk.CompressedSize}\n实际大小: {压缩数据.Length}");
                }

                压缩数据.Seek(0, SeekOrigin.Begin);
                var 压缩数据Md5 = await 实用工具.计算Md5_异步(压缩数据);
                if (!压缩数据Md5.Equals(chunk.CompressedMd5, StringComparison.OrdinalIgnoreCase))
                {
                    throw new Exception($"压缩数据MD5校验失败: {chunk.Id}\n计算MD5: {压缩数据Md5}\n正确MD5: {chunk.CompressedMd5}");
                }
                压缩数据.Seek(0, SeekOrigin.Begin);

                using (var 解压数据 = new MemoryStream())
                {
                    await 解压分块_异步(
                        chunk,
                        压缩数据,
                        解压数据,
                        md5,
                        ct
                    );

                    long 分块实际大小 = chunk.UncompressedSize;
                    Interlocked.Add(ref _已下载字节数, 分块实际大小);
                    更新进度();

                    解压数据.Seek(0, SeekOrigin.Begin);
                    await 解压数据.CopyToAsync(输出流, ct);
                }
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
                        throw new Exception($"解压数据大小不匹配: {chunk.Id}\n期望大小: {chunk.UncompressedSize}\n实际大小: {pos}");
                    }

                    if (!计算原始Md5.Equals(chunk.UncompressedMd5, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new Exception($"解压数据MD5校验失败: {chunk.Id}\n计算MD5: {计算原始Md5}\n正确MD5: {chunk.UncompressedMd5}");
                    }
                }
            }
        }

        public void 记录错误(string error)
        {
            _日志队列.Add($"[{DateTime.Now}] {error}");
        }

        private void 写入错误()
        {
            try
            {
                foreach (var message in _日志队列.GetConsumingEnumerable())
                {
                    Console.WriteLine($"错误: {message}");
                    File.AppendAllText(_错误文件路径, message + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"日志写入器错误: {ex.Message}");
            }
        }

        public void Dispose()
        {
            _cts.Cancel();
            _cts.Dispose();
            _暂停事件.Dispose();
            _日志队列.CompleteAdding();
        }
    }
}
