using ProtoBuf;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using ZstdSharp;

namespace SophonChunksDownloader
{
    public partial class Form1 : Form
    {
        private static readonly HttpClient _hc = new HttpClient();
        private static readonly string _错误文件路径 = "错误.txt";
        private const int 最大重试次数 = 3;
        private SemaphoreSlim _并发信号量;
        private readonly BlockingCollection<string> _日志队列 = new BlockingCollection<string>();
        private CancellationTokenSource _cts = new CancellationTokenSource();

        private int _总文件数 = 0;
        private int _已完成文件数 = 0;
        private long _总字节数 = 0;
        private long _已下载字节数 = 0;

        private long _上次更新字节数 = 0;
        private long _上次更新时间 = 0;
        private string _当前速度 = "0 KB/s";

        private ManifestConfig _当前配置;
        private string _保存目录;

        private bool _暂停 = false;
        private readonly ManualResetEventSlim _暂停事件 = new ManualResetEventSlim(true);
        private readonly object _暂停锁 = new object();

        public Form1()
        {
            InitializeComponent();
            Task.Run(写入错误);
        }

        private void 暂停按钮_Click(object sender, EventArgs e)
        {
            if (!_暂停)
            {
                lock (_暂停锁)
                {
                    _暂停 = true;
                    _暂停事件.Reset();
                }
                暂停按钮.Text = "继续下载";
            }
            else
            {
                lock (_暂停锁)
                {
                    _暂停 = false;
                    _暂停事件.Set();
                }
                暂停按钮.Text = "暂停下载";
            }
        }

        private async void 下载清单_Click(object sender, EventArgs e)
        {
            var 输入路径 = textBox1.Text.Trim().Trim('"');

            if (string.IsNullOrEmpty(输入路径))
            {
                MessageBox.Show("输入不能为空", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            下载清单.Enabled = false;
            label2.Text = "正在获取配置...";
            label2.Visible = true;

            try
            {
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

                try
                {
                    _当前配置 = JsonSerializer.Deserialize<ManifestConfig>(配置Json);
                }
                catch (Exception ex)
                {
                    记录错误($"配置解析失败: {ex.Message}");
                    return;
                }

                if (_当前配置.retcode != 0)
                {
                    记录错误($"配置返回错误: {_当前配置.message}");
                    return;
                }

                label2.Text = $"已获取配置，Tag: {_当前配置.data.tag}";
                选择下载框.Items.Clear();

                foreach (var manifest in _当前配置.data.manifests)
                {
                    选择下载框.Items.Add(manifest.category_name, false);
                }

                下载游戏.Enabled = true;
            }
            catch (Exception ex)
            {
                记录错误($"获取清单失败: {ex.Message}");
            }
            finally
            {
                下载清单.Enabled = true;
            }
        }

        private async void 下载游戏_Click(object sender, EventArgs e)
        {
            if (_当前配置 == null)
            {
                MessageBox.Show("请先下载清单", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (下载游戏.Text == "取消下载")
            {
                _cts.Cancel();
                return;
            }

            var 选中的文件 = new List<ManifestCategory>();
            for (int i = 0; i < 选择下载框.Items.Count; i++)
            {
                if (选择下载框.GetItemChecked(i))
                {
                    选中的文件.Add(_当前配置.data.manifests[i]);
                }
            }

            if (选中的文件.Count == 0)
            {
                MessageBox.Show("请至少选择一个下载项", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            using (var folderDialog = new FolderBrowserDialog())
            {
                if (folderDialog.ShowDialog() == DialogResult.OK)
                {
                    _保存目录 = folderDialog.SelectedPath;
                }
                else
                {
                    return;
                }
            }

            _cts?.Cancel();
            _cts?.Dispose();
            _cts = new CancellationTokenSource();

            下载游戏.Text = "取消下载";
            下载清单.Enabled = false;
            选择下载框.Enabled = false;
            暂停按钮.Enabled = true;
            label2.Text = "开始下载文件...";
            下载进度条.Value = 0;

            bool 被取消 = false;

            try
            {
                var 所有文件列表 = new List<SophonChunkFile>();
                var 文件清单字典 = new Dictionary<string, string>();

                label2.Text = "正在下载清单...";
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

                    label2.Text = $"已下载 {文件信息.category_name} 清单，包含 {清单.Chuncks.Count} 个文件";

                    foreach (var 文件 in 清单.Chuncks)
                    {
                        所有文件列表.Add(文件);
                        文件清单字典[文件.File] = 分块地址前缀;
                    }
                }

                _总文件数 = 所有文件列表.Count;
                if (_总文件数 == 0)
                {
                    label2.Text = "没有找到任何需要下载的文件";
                    return;
                }

                _总字节数 = 所有文件列表.Sum(a => a.Size);
                label2.Text = $"文件总数：{_总文件数} ，共 {实用工具.格式化文件大小(_总字节数)}";
                _已完成文件数 = 0;
                _已下载字节数 = 0;

                _上次更新字节数 = 0;
                _上次更新时间 = Environment.TickCount;
                _当前速度 = "0 KB/s";

                int 最大并发 = 16;
                _并发信号量 = new SemaphoreSlim(最大并发, 最大并发);

                var 下载任务 = new List<Task>();
                foreach (var 文件 in 所有文件列表)
                {
                    if (_cts.IsCancellationRequested) break;
                    await _并发信号量.WaitAsync(_cts.Token);

                    下载任务.Add(Task.Run(async () =>
                    {
                        try
                        {
                            await 下载文件_异步(文件, 文件清单字典[文件.File], _保存目录, _cts.Token);
                        }
                        catch (OperationCanceledException)
                        {
                            // 取消
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
                    label2.Text = "下载完成！";
                    下载进度条.Value = 100;
                    MessageBox.Show("下载已完成！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    被取消 = true;
                }
            }
            catch (OperationCanceledException)
            {
                被取消 = true;
            }
            catch (Exception ex)
            {
                记录错误($"下载失败: {ex.Message}");
                MessageBox.Show($"下载失败: \n{ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                下载游戏.Text = "下载游戏";
                下载清单.Enabled = true;
                选择下载框.Enabled = true;
                暂停按钮.Enabled = false;
                _暂停 = false;
                _暂停事件.Set();

                if (被取消)
                {
                    Action 重置状态 = () =>
                    {
                        _总文件数 = 0;
                        _已完成文件数 = 0;
                        _总字节数 = 0;
                        _已下载字节数 = 0;
                        _上次更新字节数 = 0;
                        _当前速度 = "0 KB/s";
                        下载进度条.Value = 0;
                        label2.Text = "";
                    };

                    if (InvokeRequired)
                        Invoke(重置状态);
                    else
                        重置状态();

                    MessageBox.Show("下载已取消", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
        }

        private async Task 下载文件_异步(SophonChunkFile file, string 分块路径前缀, string 保存路径, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            _暂停事件.Wait(ct);

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
            if (InvokeRequired)
            {
                Invoke(new Action(更新进度));
                return;
            }

            var 进度百分比 = (int)((double)_已下载字节数 / _总字节数 * 100);
            下载进度条.Value = Math.Min(100, Math.Max(0, 进度百分比));

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

            label2.Text = $"已完成: {_已完成文件数}/{_总文件数}, " +
                          $"{实用工具.格式化文件大小(_已下载字节数)}/{实用工具.格式化文件大小(_总字节数)} " +
                          $"[速度: {_当前速度}]";
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
                    var 计算原始Md5 = BitConverter.ToString(原始Md5.Hash).Replace("-", "").ToLower();

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

        private void 记录错误(string error)
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
                    File.AppendAllText(_错误文件路径, message + Environment.NewLine);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"日志写入器错误: {ex.Message}");
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _暂停事件?.Dispose();
            base.OnFormClosed(e);
        }
    }
}
