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
        private static readonly string _�����ļ�·�� = "����.txt";
        private const int ������Դ��� = 3;
        private SemaphoreSlim _�����ź���;
        private readonly BlockingCollection<string> _��־���� = new BlockingCollection<string>();
        private CancellationTokenSource _cts = new CancellationTokenSource();

        private int _���ļ��� = 0;
        private int _������ļ��� = 0;
        private long _���ֽ��� = 0;
        private long _�������ֽ��� = 0;

        private long _�ϴθ����ֽ��� = 0;
        private long _�ϴθ���ʱ�� = 0;
        private string _��ǰ�ٶ� = "0 KB/s";

        private ManifestConfig _��ǰ����;
        private string _����Ŀ¼;

        public Form1()
        {
            InitializeComponent();
            Task.Run(д�����);
        }

        private async void �����嵥_Click(object sender, EventArgs e)
        {
            var ����·�� = textBox1.Text.Trim().Trim('"');

            if (string.IsNullOrEmpty(����·��))
            {
                MessageBox.Show("���벻��Ϊ��", "��ʾ", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            �����嵥.Enabled = false;
            label2.Text = "���ڻ�ȡ����...";
            label2.Visible = true;

            try
            {
                string ����Json;
                try
                {
                    if (����·��.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                        || ����·��.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    {
                        using (var rsp = await _hc.GetAsync(����·��, _cts.Token))
                        {
                            rsp.EnsureSuccessStatusCode();
                            ����Json = await rsp.Content.ReadAsStringAsync(_cts.Token);
                        }
                    }
                    else
                    {
                        var �ļ�·�� = Path.GetFullPath(����·��);
                        if (!File.Exists(�ļ�·��))
                        {
                            throw new FileNotFoundException($"�����ļ�������: {�ļ�·��}");
                        }
                        ����Json = await File.ReadAllTextAsync(�ļ�·��, _cts.Token);
                    }
                }
                catch (Exception ex)
                {
                    ��¼����($"���û�ȡʧ��: {ex.Message}");
                    return;
                }

                try
                {
                    _��ǰ���� = JsonSerializer.Deserialize<ManifestConfig>(����Json);
                }
                catch (Exception ex)
                {
                    ��¼����($"���ý���ʧ��: {ex.Message}");
                    return;
                }

                if (_��ǰ����.retcode != 0)
                {
                    ��¼����($"���÷��ش���: {_��ǰ����.message}");
                    return;
                }

                label2.Text = $"�ѻ�ȡ���ã�Tag: {_��ǰ����.data.tag}";
                ѡ�����ؿ�.Items.Clear();

                foreach (var manifest in _��ǰ����.data.manifests)
                {
                    ѡ�����ؿ�.Items.Add(manifest.category_name, false);
                }

                ������Ϸ.Enabled = true;
            }
            catch (Exception ex)
            {
                ��¼����($"��ȡ�嵥ʧ��: {ex.Message}");
            }
            finally
            {
                �����嵥.Enabled = true;
            }
        }

        private async void ������Ϸ_Click(object sender, EventArgs e)
        {
            if (_��ǰ���� == null)
            {
                MessageBox.Show("���������嵥", "��ʾ", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var ѡ�е��ļ� = new List<ManifestCategory>();
            for (int i = 0; i < ѡ�����ؿ�.Items.Count; i++)
            {
                if (ѡ�����ؿ�.GetItemChecked(i))
                {
                    ѡ�е��ļ�.Add(_��ǰ����.data.manifests[i]);
                }
            }

            if (ѡ�е��ļ�.Count == 0)
            {
                MessageBox.Show("������ѡ��һ��������", "��ʾ", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            using (var folderDialog = new FolderBrowserDialog())
            {
                if (folderDialog.ShowDialog() == DialogResult.OK)
                {
                    _����Ŀ¼ = folderDialog.SelectedPath;
                }
                else
                {
                    return;
                }
            }

            ������Ϸ.Enabled = false;
            �����嵥.Enabled = false;
            ѡ�����ؿ�.Enabled = false;
            label2.Text = "��ʼ�����ļ�...";
            ���ؽ�����.Value = 0;

            try
            {
                var �����ļ��б� = new List<SophonChunkFile>();
                var �ļ��嵥�ֵ� = new Dictionary<SophonChunkFile, string>();

                label2.Text = "���������嵥...";
                foreach (var �ļ���Ϣ in ѡ�е��ļ�)
                {
                    var �嵥��ַǰ׺ = ʵ�ù���.ȷ��б�ܽ�β(�ļ���Ϣ.manifest_download.url_prefix);
                    var �嵥Id = �ļ���Ϣ.manifest.id;
                    var �ֿ��ַǰ׺ = ʵ�ù���.ȷ��б�ܽ�β(�ļ���Ϣ.chunk_download.url_prefix);

                    byte[] �嵥����;
                    try
                    {
                        using (var rsp = await _hc.GetAsync(�嵥��ַǰ׺ + �嵥Id, HttpCompletionOption.ResponseHeadersRead, _cts.Token))
                        {
                            rsp.EnsureSuccessStatusCode();
                            �嵥���� = await rsp.Content.ReadAsByteArrayAsync(_cts.Token);
                        }
                    }
                    catch (Exception ex)
                    {
                        ��¼����($"�嵥����ʧ��: {�嵥��ַǰ׺}{�嵥Id}\n{ex.Message}");
                        continue;
                    }

                    using var decompressor = new Decompressor();
                    var ��ѹ�嵥 = decompressor.Unwrap(�嵥����);
                    var �嵥 = Serializer.Deserialize<SophonChunkManifest>(��ѹ�嵥);

                    var ��С = �嵥.Chuncks.Sum(a => a.Size);
                    label2.Text = $"������ {�ļ���Ϣ.category_name} �嵥������ {�嵥.Chuncks.Count} ���ļ�";

                    foreach (var �ļ� in �嵥.Chuncks)
                    {
                        �����ļ��б�.Add(�ļ�);
                        �ļ��嵥�ֵ�[�ļ�] = �ֿ��ַǰ׺;
                    }
                }

                _���ļ��� = �����ļ��б�.Count;
                if (_���ļ��� == 0)
                {
                    label2.Text = "û���ҵ��κ���Ҫ���ص��ļ�";
                    return;
                }

                _���ֽ��� = �����ļ��б�.Sum(a => a.Size);
                label2.Text = $"�ļ�������{_���ļ���} ���� {ʵ�ù���.��ʽ���ļ���С(_���ֽ���)}";
                _������ļ��� = 0;
                _�������ֽ��� = 0;

                _�ϴθ����ֽ��� = 0;
                _�ϴθ���ʱ�� = Environment.TickCount;
                _��ǰ�ٶ� = "0 KB/s";

                int ��󲢷� = 16;
                _�����ź��� = new SemaphoreSlim(��󲢷�, ��󲢷�);

                var �������� = new List<Task>();
                foreach (var �ļ� in �����ļ��б�)
                {
                    if (_cts.IsCancellationRequested) break;
                    await _�����ź���.WaitAsync(_cts.Token);

                    ��������.Add(Task.Run(async () =>
                    {
                        try
                        {
                            await �����ļ�_�첽(�ļ�, �ļ��嵥�ֵ�[�ļ�], _����Ŀ¼, _cts.Token);
                        }
                        catch (OperationCanceledException)
                        {
                        }
                        catch (Exception ex)
                        {
                            ��¼����($"�ļ� {�ļ�.File} ����ʧ��: {ex.Message}");
                        }
                        finally
                        {
                            _�����ź���.Release();
                        }
                    }, _cts.Token));
                }

                await Task.WhenAll(��������);

                if (!_cts.IsCancellationRequested)
                {
                    label2.Text = "������ɣ�";
                    ���ؽ�����.Value = 100;

                    MessageBox.Show("��������ɣ�", "��ʾ", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch (Exception ex)
            {
                ��¼����($"����ʧ��: {ex.Message}");
                MessageBox.Show($"����ʧ��: \n{ex.Message}", "����", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                ������Ϸ.Enabled = true;
                �����嵥.Enabled = true;
                ѡ�����ؿ�.Enabled = false;
            }
        }

        private async Task �����ļ�_�첽(SophonChunkFile file, string �ֿ�·��ǰ׺, string ����·��, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            var filePath = Path.Combine(����·��, file.File);
            var tmpPath = filePath + ".tmp";
            if (File.Exists(tmpPath))
                File.Delete(tmpPath);

            Directory.CreateDirectory(Path.GetDirectoryName(filePath));

            if (File.Exists(filePath))
            {
                using (var fs = File.OpenRead(filePath))
                {
                    var �����ļ�Md5 = await ʵ�ù���.����Md5_�첽(fs);
                    if (�����ļ�Md5.Equals(file.Md5, StringComparison.OrdinalIgnoreCase))
                    {
                        Interlocked.Add(ref _�������ֽ���, file.Size);
                        var ���ļ��� = Interlocked.Increment(ref _������ļ���);
                        ���½���();
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
                        await ����ֿ�_�첽(chunk, �ֿ�·��ǰ׺, fs, md5, ct);
                    }

                    var ����Md5 = BitConverter.ToString(md5.GetHashAndReset()).Replace("-", "").ToLower();

                    if (!����Md5.Equals(file.Md5, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new Exception($"�ļ�MD5У��ʧ��: {filePath}\n����Md5: {����Md5}\n��ȷMD5: {file.Md5}");
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
                var ���ļ��� = Interlocked.Increment(ref _������ļ���);
                ���½���();
            }
        }

        private void ���½���()
        {
            if (InvokeRequired)
            {
                Invoke(new Action(���½���));
                return;
            }

            var ���Ȱٷֱ� = (int)((double)_�������ֽ��� / _���ֽ��� * 100);
            ���ؽ�����.Value = Math.Min(100, Math.Max(0, ���Ȱٷֱ�));

            long ��ǰʱ�� = Environment.TickCount;
            long ʱ��� = ��ǰʱ�� - _�ϴθ���ʱ��;

            if (ʱ��� > 100)
            {
                long �ֽ����� = _�������ֽ��� - _�ϴθ����ֽ���;
                double �ٶ� = (�ֽ����� * 1000.0) / ʱ���; // �ֽ�/��
                _��ǰ�ٶ� = ʵ�ù���.��ʽ���ٶ�(�ٶ�);

                _�ϴθ����ֽ��� = _�������ֽ���;
                _�ϴθ���ʱ�� = ��ǰʱ��;
            }

            label2.Text = $"�����: {_������ļ���}/{_���ļ���}, " +
                          $"{ʵ�ù���.��ʽ���ļ���С(_�������ֽ���)}/{ʵ�ù���.��ʽ���ļ���С(_���ֽ���)} " +
                          $"[�ٶ�: {_��ǰ�ٶ�}]";
        }

        private async Task ����ֿ�_�첽(
            SophonChunk chunk,
            string �ֿ�·��ǰ׺,
            FileStream �����,
            IncrementalHash md5,
            CancellationToken ct)
        {
            var url = �ֿ�·��ǰ׺ + chunk.Id;
            Exception �쳣 = null;

            using (var ѹ������ = new MemoryStream())
            {
                for (int i = 0; i < ������Դ���; i++)
                {
                    try
                    {
                        ѹ������.SetLength(0);
                        using (var response = await _hc.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct))
                        {
                            response.EnsureSuccessStatusCode();
                            await response.Content.CopyToAsync(ѹ������, ct);
                            break;
                        }
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        �쳣 = ex;
                        if (i < ������Դ��� - 1)
                        {
                            await Task.Delay((int)Math.Pow(2, i) * 1000, ct);
                        }
                    }
                }

                if (ѹ������.Length == 0)
                {
                    throw new Exception($"����ʧ��(����{������Դ���}��): {url}\nԭ��: {�쳣?.Message}");
                }

                if (ѹ������.Length != chunk.CompressedSize)
                {
                    throw new Exception($"ѹ�����ݴ�С��ƥ��: {chunk.Id}\n������С: {chunk.CompressedSize}\nʵ�ʴ�С: {ѹ������.Length}");
                }

                ѹ������.Seek(0, SeekOrigin.Begin);
                var ѹ������Md5 = await ʵ�ù���.����Md5_�첽(ѹ������);
                if (!ѹ������Md5.Equals(chunk.CompressedMd5, StringComparison.OrdinalIgnoreCase))
                {
                    throw new Exception($"ѹ������MD5У��ʧ��: {chunk.Id}\n����MD5: {ѹ������Md5}\n��ȷMD5: {chunk.CompressedMd5}");
                }
                ѹ������.Seek(0, SeekOrigin.Begin);

                using (var ��ѹ���� = new MemoryStream())
                {
                    await ��ѹ�ֿ�_�첽(
                        chunk,
                        ѹ������,
                        ��ѹ����,
                        md5,
                        ct
                    );

                    long �ֿ�ʵ�ʴ�С = chunk.UncompressedSize;
                    Interlocked.Add(ref _�������ֽ���, �ֿ�ʵ�ʴ�С);
                    ���½���();

                    ��ѹ����.Seek(0, SeekOrigin.Begin);
                    await ��ѹ����.CopyToAsync(�����, ct);
                }
            }
        }

        private static async Task ��ѹ�ֿ�_�첽(
            SophonChunk chunk,
            Stream ѹ��������,
            Stream �����,
            IncrementalHash md5,
            CancellationToken ct)
        {
            using (var ��ѹ�� = new DecompressionStream(ѹ��������))
            {
                const int ��������С = 524288;
                var ������ = new byte[��������С];
                long pos = 0;
                using (var ԭʼMd5 = MD5.Create())
                {
                    while (true)
                    {
                        ct.ThrowIfCancellationRequested();
                        var �����ֽ��� = await ��ѹ��.ReadAsync(������, 0, ������.Length, ct);
                        if (�����ֽ��� == 0) break;

                        await �����.WriteAsync(������, 0, �����ֽ���, ct);
                        ԭʼMd5.TransformBlock(������, 0, �����ֽ���, null, 0);
                        md5.AppendData(������, 0, �����ֽ���);
                        pos += �����ֽ���;
                    }

                    ԭʼMd5.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                    var ����ԭʼMd5 = BitConverter.ToString(ԭʼMd5.Hash).Replace("-", "").ToLower();

                    if (pos != chunk.UncompressedSize)
                    {
                        throw new Exception($"��ѹ���ݴ�С��ƥ��: {chunk.Id}\n������С: {chunk.UncompressedSize}\nʵ�ʴ�С: {pos}");
                    }

                    if (!����ԭʼMd5.Equals(chunk.UncompressedMd5, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new Exception($"��ѹ����MD5У��ʧ��: {chunk.Id}\n����MD5: {����ԭʼMd5}\n��ȷMD5: {chunk.UncompressedMd5}");
                    }
                }
            }
        }

        private void ��¼����(string error)
        {
            _��־����.Add($"[{DateTime.Now}] {error}");
        }

        private void д�����()
        {
            try
            {
                foreach (var message in _��־����.GetConsumingEnumerable())
                {
                    Console.WriteLine($"����: {message}");
                    File.AppendAllText(_�����ļ�·��, message + Environment.NewLine);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"��־д��������: {ex.Message}");
            }
        }
    }
}
