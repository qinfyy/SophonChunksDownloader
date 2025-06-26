using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using ProtoBuf;
using ZstdSharp;

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
                Console.Write("请输入清单下载地址前缀：");
                var 清单地址前缀 = Console.ReadLine()?.Trim();

                Console.Write("请输入清单ID：");
                var 清单Id = Console.ReadLine()?.Trim();

                Console.Write("请输入文件下载地址前缀：");
                var 分块地址前缀 = Console.ReadLine()?.Trim();

                Console.Write("请输入文件保存目录：");
                var 保存目录 = Console.ReadLine()?.Trim();

                if (string.IsNullOrEmpty(清单地址前缀) ||
                    string.IsNullOrEmpty(清单Id) ||
                    string.IsNullOrEmpty(分块地址前缀) ||
                    string.IsNullOrEmpty(保存目录))
                {
                    Console.WriteLine("输入参数不能为空");
                    return;
                }

                清单地址前缀 = 确保斜杠结尾(清单地址前缀);
                分块地址前缀 = 确保斜杠结尾(分块地址前缀);

                if (File.Exists(_错误文件路径))
                {
                    File.Delete(_错误文件路径);
                }

                File.Create(_错误文件路径).Close();

                Console.WriteLine("\n开始下载清单...");
                byte[] 清单数据;
                try
                {
                    using (var rsp = await _hc.GetAsync(清单地址前缀 + 清单Id, HttpCompletionOption.ResponseHeadersRead, _cts.Token))
                    {
                        rsp.EnsureSuccessStatusCode();
                        清单数据 = await rsp.Content.ReadAsByteArrayAsync(_cts.Token);
                    }
                }
                catch
                {
                    记录错误($"清单下载失败: {清单地址前缀}{清单Id}");
                    return;
                }

                Console.WriteLine($"清单下载完成");
                Console.WriteLine("正在解压清单...");

                using var decompressor = new Decompressor();
                var 解压清单 = decompressor.Unwrap(清单数据);
                var 清单 = Serializer.Deserialize<SophonChunkManifest>(解压清单);
                GC.Collect();

                var 总大小 = 清单.Chuncks.Sum(a => a.Size);
                Console.WriteLine($"文件数量：{清单.Chuncks.Count} ，共 {实用工具.格式化文件大小(总大小)}");
                Console.WriteLine("开始下载文件...\n");

                _计时器.Start();
                var 下载任务 = new List<Task>();
                foreach (var 文件 in 清单.Chuncks)
                {
                    if (_cts.IsCancellationRequested) break;
                    await _并发信号量.WaitAsync(_cts.Token);

                    下载任务.Add(Task.Run(async () =>
                    {
                        try
                        {
                            await 下载文件_异步(文件, 分块地址前缀, 保存目录, _cts.Token);
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

        private static string 确保斜杠结尾(string url)
        {
            return url.EndsWith("/") ? url : url + "/";
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
                        Console.WriteLine($"文件已存在且MD5匹配，跳过下载: {filePath}");
                        return;
                    }
                }
            }

            try
            {
                Console.WriteLine($"开始下载文件: {file.File}");
                var 分块数量 = file.Chunks.Count;

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
                            $"计算MD5: {计算Md5}\n正确MD5: {file.Md5}");
                    }
                }

                File.Move(tmpPath, filePath, true);
                Console.WriteLine($"文件下载完成: {filePath}");
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