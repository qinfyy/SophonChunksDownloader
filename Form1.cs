using ProtoBuf;
using System.Text.Json;
using ZstdSharp;

namespace SophonChunksDownloader
{
    public partial class Form1 : Form
    {
        private ManifestConfig? _当前配置;
        private string? _保存目录;
        private Downloader? _下载器;

        private static readonly HttpClient _hc = new HttpClient();

        public Form1()
        {
            InitializeComponent();
        }

        private void 暂停按钮_Click(object sender, EventArgs e)
        {
            if (_下载器 == null) return;

            _下载器.暂停或继续();
            暂停按钮.Text = _下载器.是否暂停 ? "继续下载" : "暂停下载";
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
                        using (var rsp = await _hc.GetAsync(输入路径))
                        {
                            rsp.EnsureSuccessStatusCode();
                            配置Json = await rsp.Content.ReadAsStringAsync();
                        }
                    }
                    else
                    {
                        var 文件路径 = Path.GetFullPath(输入路径);
                        if (!File.Exists(文件路径))
                        {
                            throw new FileNotFoundException($"配置文件不存在: {文件路径}");
                        }
                        配置Json = await File.ReadAllTextAsync(文件路径);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"配置获取失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                try
                {
                    _当前配置 = JsonSerializer.Deserialize<ManifestConfig>(配置Json);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"配置解析失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                if (_当前配置.retcode != 0)
                {
                    MessageBox.Show($"配置返回错误: {_当前配置.message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
                MessageBox.Show($"获取清单失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
                _下载器?.取消下载();
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

            // 释放旧下载器
            _下载器?.Dispose();
            _下载器 = new Downloader();

            // 设置回调
            _下载器.进度更新回调 = (progress) =>
            {
                if (InvokeRequired)
                {
                    Invoke(new Action(() => 更新进度UI(progress)));
                }
                else
                {
                    更新进度UI(progress);
                }
            };

            _下载器.状态文本回调 = (text) =>
            {
                if (InvokeRequired)
                {
                    Invoke(new Action(() => label2.Text = text));
                }
                else
                {
                    label2.Text = text;
                }
            };

            _下载器.下载完成回调 = () =>
            {
                if (InvokeRequired)
                {
                    Invoke(new Action(() =>
                    {
                        label2.Text = "下载完成！";
                        下载进度条.Value = 100;
                        MessageBox.Show("下载已完成！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        重置UI状态();
                    }));
                }
                else
                {
                    label2.Text = "下载完成！";
                    下载进度条.Value = 100;
                    MessageBox.Show("下载已完成！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    重置UI状态();
                }
            };

            _下载器.下载取消回调 = () =>
            {
                if (InvokeRequired)
                {
                    Invoke(new Action(() =>
                    {
                        MessageBox.Show("下载已取消", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        重置UI状态();
                    }));
                }
                else
                {
                    MessageBox.Show("下载已取消", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    重置UI状态();
                }
            };

            // UI 状态更新
            下载游戏.Text = "取消下载";
            下载清单.Enabled = false;
            选择下载框.Enabled = false;
            暂停按钮.Enabled = true;
            label2.Text = "开始下载文件...";
            下载进度条.Value = 0;

            // 清单解析逻辑保持在 UI（按要求）
            var 所有文件列表 = new List<SophonChunkFile>();
            var 文件清单字典 = new Dictionary<string, string>();

            try
            {
                label2.Text = "正在下载清单...";
                foreach (var 文件信息 in 选中的文件)
                {
                    var 清单地址前缀 = 实用工具.确保斜杠结尾(文件信息.manifest_download.url_prefix);
                    var 清单Id = 文件信息.manifest.id;
                    var 分块地址前缀 = 实用工具.确保斜杠结尾(文件信息.chunk_download.url_prefix);

                    byte[] 清单数据;
                    try
                    {
                        using (var rsp = await _hc.GetAsync(清单地址前缀 + 清单Id))
                        {
                            rsp.EnsureSuccessStatusCode();
                            清单数据 = await rsp.Content.ReadAsByteArrayAsync();
                        }
                    }
                    catch (Exception ex)
                    {
                        _下载器.记录错误($"清单下载失败: {清单地址前缀}{清单Id}\n{ex.Message}");
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

                // 启动下载
                await _下载器.开始下载(所有文件列表, 文件清单字典, _保存目录);
            }
            catch (Exception ex)
            {
                _下载器.记录错误($"下载启动失败: {ex.Message}");
                MessageBox.Show($"下载失败: \n{ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                重置UI状态();
            }
        }

        private void 更新进度UI(DownloadProgress progress)
        {
            var 进度百分比 = (int)((double)progress.已下载字节数 / progress.总字节数 * 100);
            下载进度条.Value = Math.Min(100, Math.Max(0, 进度百分比));
            label2.Text = progress.状态文本;
        }

        private void 重置UI状态()
        {
            下载游戏.Text = "下载游戏";
            下载清单.Enabled = true;
            选择下载框.Enabled = true;
            暂停按钮.Enabled = false;
            暂停按钮.Text = "暂停下载";
            下载进度条.Value = 0;
            label2.Text = "";
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _下载器?.Dispose();
            base.OnFormClosed(e);
        }
    }
}