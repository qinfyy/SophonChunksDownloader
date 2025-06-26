using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using ProtoBuf;
using ZstdSharp;
using ShellProgressBar;

namespace SophonChunksDownloader
{
    class Program
    {
        private static readonly HttpClient _hc = new HttpClient();
        private static readonly string _错误文件路径 = "错误.txt";
        private const int 最大重试次数 = 3;
        private static readonly SemaphoreSlim _并发信号量;
        private static readonly BlockingCollection<string> _日志队列 = new BlockingCollection<string>();
        private static readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private static readonly Stopwatch _计时器 = new Stopwatch();

        private static int _总文件数 = 0;
        private static int _已完成文件数 = 0;

        static Program()
        {
            int 最大并发 = 16;
            _并发信号量 = new SemaphoreSlim(最大并发, 最大并发);
            Task.Run(写入错误);
        }

        static async Task Main(string[] args)
        {
            Console.CancelKeyPress += (s, e) => {
                e.Cancel = true;
                _cts.Cancel();
                Console.WriteLine("正在取消操作，请稍候...");
            };

            try
            {
                Console.Write("请输入清单配置的URL：");
                var 输入路径 = Console.ReadLine()?.Trim().Trim('"');

                if (string.IsNullOrEmpty(输入路径))
                {
                    Console.WriteLine("输入不能为空");
                    return;
                }

                Console.WriteLine("\n开始获取配置...");
                string 配置Json;
                try
                {
                    if (输入路径.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                        || 输入路径.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    {
                        using (var rsp = await _hc.GetAsync(输入路径, _cts.Token))
                        {
                            rsp.EnsureSuccessStatusCode();
                            配置Json = await rsp.Content.ReadAsStringAsync(_cts.Token);
                        }
                    }
                    else
                    {
                        var 文件路径 = Path.GetFullPath(输入路径);
                        if (!File.Exists(文件路径))
                        {
                            throw new FileNotFoundException($"配置文件不存在: {文件路径}");
                        }
                        配置Json = await File.ReadAllTextAsync(文件路径, _cts.Token);
                    }
                }
                catch (Exception ex)
                {
                    记录错误($"配置获取失败: {ex.Message}");
                    return;
                }

                ManifestConfig 配置;
                try
                {
                    配置 = JsonSerializer.Deserialize<ManifestConfig>(配置Json);
                }
                catch (Exception ex)
                {
                    记录错误($"配置解析失败: {ex.Message}");
                    return;
                }

                if (配置.retcode != 0)
                {
                    记录错误($"配置返回错误: {配置.message}");
                    return;
                }

                Console.WriteLine("\nTag: " + 配置.data.tag);
                Console.WriteLine("请选择要下载的文件:");
                for (int i = 0; i < 配置.data.manifests.Count; i++)
                {
                    Console.WriteLine($"{i + 1}. {配置.data.manifests[i].category_name}");
                }

                Console.Write($"\n输入选择 (1-{配置.data.manifests.Count}): ");
                var 选择 = Console.ReadLine();
                var 选择的索引 = new List<int>();
                var 选中的文件 = new List<ManifestCategory>();

                foreach (var c in 选择)
                {
                    if (c == ',' || c == ' ') continue;
                    if (int.TryParse(c.ToString(), out int index) && index >= 1 && index <= 配置.data.manifests.Count)
                    {
                        if (!选择的索引.Contains(index))
                            选择的索引.Add(index);
                    }
                }

                if (选择的索引.Count == 0)
                {
                    Console.WriteLine("输入无效，没有选择任何文件");
                    return;
                }

                选中的文件 = 选择的索引
                    .OrderBy(i => i)
                    .Select(i => 配置.data.manifests[i - 1])
                    .ToList();

                Console.Write("请输入文件保存目录：");
                var 保存目录 = Console.ReadLine()?.Trim();

                if (string.IsNullOrEmpty(保存目录))
                {
                    Console.WriteLine("保存目录不能为空");
                    return;
                }

                if (File.Exists(_错误文件路径))
                {
                    File.Delete(_错误文件路径);
                }

                var 所有文件列表 = new List<SophonChunkFile>();
                var 文件清单字典 = new Dictionary<SophonChunkFile, string>(); // 存储文件对应的分块路径前缀

                Console.WriteLine("\n开始下载清单...");
                foreach (var 文件信息 in 选中的文件)
                {
                    var 清单地址前缀 = 实用工具.确保斜杠结尾(文件信息.manifest_download.url_prefix);
                    var 清单Id = 文件信息.manifest.id;
                    var 分块地址前缀 = 实用工具.确保斜杠结尾(文件信息.chunk_download.url_prefix);

                    byte[] 清单数据;
                    try
                    {
                        using (var rsp = await _hc.GetAsync(清单地址前缀 + 清单Id, HttpCompletionOption.ResponseHeadersRead, _cts.Token))
                        {
                            rsp.EnsureSuccessStatusCode();
                            清单数据 = await rsp.Content.ReadAsByteArrayAsync(_cts.Token);
                        }
                    }
                    catch (Exception ex)
                    {
                        记录错误($"清单下载失败: {清单地址前缀}{清单Id}\n{ex.Message}");
                        continue;
                    }

                    using var decompressor = new Decompressor();
                    var 解压清单 = decompressor.Unwrap(清单数据);
                    var 清单 = Serializer.Deserialize<SophonChunkManifest>(解压清单);

                    var 大小 = 清单.Chuncks.Sum(a => a.Size);
                    Console.WriteLine($"已下载 {文件信息.category_name} 清单，包含 {清单.Chuncks.Count} 个文件，共 {实用工具.格式化文件大小(大小)}");

                    foreach (var 文件 in 清单.Chuncks)
                    {
                        所有文件列表.Add(文件);
                        文件清单字典[文件] = 分块地址前缀;
                    }
                }

                _总文件数 = 所有文件列表.Count;
                if (_总文件数 == 0)
                {
                    Console.WriteLine("没有找到任何需要下载的文件");
                    return;
                }

                var 总大小 = 所有文件列表.Sum(a => a.Size);
                Console.WriteLine($"\n文件总数：{_总文件数} ，共 {实用工具.格式化文件大小(总大小)}");
                Console.WriteLine("开始下载文件...\n");
                GC.Collect();

                ProgressBarOptions.ProgressMessageEncodingName = "GBK";
                var 进度条设置 = new ProgressBarOptions
                {
                    DisplayTimeInRealTime = true,
                    CollapseWhenFinished = false,
                    ForegroundColor = ConsoleColor.White,
                    BackgroundColor = ConsoleColor.DarkGray,
                    ProgressCharacter = '─'
                };

                _计时器.Start();

                using (var 主进度条 = new ProgressBar(_总文件数, "下载进度", 进度条设置))
                {
                    主进度条.Tick(0);

                    var 下载任务 = new List<Task>();
                    foreach (var 文件 in 所有文件列表)
                    {
                        if (_cts.IsCancellationRequested) break;
                        await _并发信号量.WaitAsync(_cts.Token);

                        var 分块地址前缀 = 文件清单字典[文件];
                        下载任务.Add(Task.Run(async () =>
                        {
                            try
                            {
                                await 下载文件_异步(文件, 分块地址前缀, 保存目录, _cts.Token);

                                var newCount = Interlocked.Increment(ref _已完成文件数);
                                主进度条.Tick(newCount, $"已完成: {newCount}/{_总文件数}");
                            }
                            catch (OperationCanceledException)
                            {
                            }
                            catch (Exception ex)
                            {
                                记录错误($"文件 {文件.File} 下载失败: {ex.Message}");
                            }
                            finally
                            {
                                _并发信号量.Release();
                            }
                        }, _cts.Token));
                    }

                    await Task.WhenAll(下载任务);
                }

                if (!_cts.IsCancellationRequested)
                {
                    _计时器.Stop();
                    var elapsed = _计时器.Elapsed;
                    Console.WriteLine($"下载用时：{elapsed.Hours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}");

                    Console.WriteLine("\n所有文件下载完成!");
                }
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("\n操作已被取消");
            }
            catch (Exception ex)
            {
                记录错误($"程序异常: {ex.Message}");
            }
            finally
            {
                _日志队列.CompleteAdding();
                await Task.Delay(100);
            }
        }

        private static async Task 下载文件_异步(SophonChunkFile file, string 分块路径前缀, string 保存路径, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            var filePath = Path.Combine(保存路径, file.File);
            var tmpPath = filePath + ".tmp";
            if (File.Exists(tmpPath))
                File.Delete(tmpPath);

            Directory.CreateDirectory(Path.GetDirectoryName(filePath));

            if (File.Exists(filePath))
            {
                using (var fs = File.OpenRead(filePath))
                {
                    var 存在文件Md5 = await 实用工具.计算Md5_异步(fs);
                    if (存在文件Md5.Equals(file.Md5, StringComparison.OrdinalIgnoreCase))
                    {
                        //Console.WriteLine($"文件已存在且MD5匹配，跳过下载: {filePath}");
                        return;
                    }
                }
            }

            try
            {
                //Console.WriteLine($"开始下载文件: {file.File}");

                using (var fs = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, FileOptions.Asynchronous))
                using (var md5 = IncrementalHash.CreateHash(HashAlgorithmName.MD5))
                {
                    foreach (var chunk in file.Chunks)
                    {
                        ct.ThrowIfCancellationRequested();

                        await 处理分块_异步(chunk, 分块路径前缀, fs, md5, ct);
                    }

                    var 计算Md5 = BitConverter.ToString(md5.GetHashAndReset()).Replace("-", "").ToLower();

                    if (!计算Md5.Equals(file.Md5, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new Exception($"文件MD5校验失败: {filePath}\n" +
                            $"计算Md5: {计算Md5}\n正确MD5: {file.Md5}");
                    }
                }

                File.Move(tmpPath, filePath, true);
                //Console.WriteLine($"文件下载完成: {filePath}");
            }
            catch
            {
                if (File.Exists(tmpPath))
                    File.Delete(tmpPath);
                throw;
            }
            finally
            {
                GC.Collect();
            }
        }

        private static async Task 处理分块_异步(
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
                    throw new Exception($"压缩数据大小不匹配: {chunk.Id}\n" +
                        $"期望大小: {chunk.CompressedSize}\n实际大小: {压缩数据.Length}");
                }

                压缩数据.Seek(0, SeekOrigin.Begin);
                var 压缩数据Md5 = await 实用工具.计算Md5_异步(压缩数据);
                if (!压缩数据Md5.Equals(chunk.CompressedMd5, StringComparison.OrdinalIgnoreCase))
                {
                    throw new Exception($"压缩数据MD5校验失败: {chunk.Id}\n" +
                        $"计算MD5: {压缩数据Md5}\n正确MD5: {chunk.CompressedMd5}");
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
                    var 计算原始Md5 = BitConverter.ToString(原始Md5.Hash).Replace("-", "").ToLower();

                    if (pos != chunk.UncompressedSize)
                    {
                        throw new Exception($"解压数据大小不匹配: {chunk.Id}\n" +
                            $"期望大小: {chunk.UncompressedSize}\n实际大小: {pos}");
                    }

                    if (!计算原始Md5.Equals(chunk.UncompressedMd5, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new Exception($"解压数据MD5校验失败: {chunk.Id}\n" +
                            $"计算MD5: {计算原始Md5}\n正确MD5: {chunk.UncompressedMd5}");
                    }
                }
            }
        }

        private static void 记录错误(string error)
        {
            _日志队列.Add($"[{DateTime.Now}] {error}");
        }

        private static void 写入错误()
        {
            try
            {
                foreach (var message in _日志队列.GetConsumingEnumerable())
                {
                    Console.WriteLine($"错误: {message}");
                    File.AppendAllText(_错误文件路径, message + Environment.NewLine);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"日志写入器错误: {ex.Message}");
            }
        }
    }
}