using System.Security.Cryptography;

namespace SophonChunksDownloader
{
    public static class 实用工具
    {
        public static string 计算Md5(byte[] data)
        {
            using (var md5 = MD5.Create())
            {
                var hash = md5.ComputeHash(data);
                return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }
        }

        public static async Task<string> 计算Md5_异步(Stream stream)
        {
            using (var md5 = MD5.Create())
            {
                var hash = await md5.ComputeHashAsync(stream);
                return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }
        }

        public static string 格式化文件大小(long 字节数, int 保留小数 = 2)
        {
            if (字节数 < 0)
                throw new ArgumentException("文件大小不能为负数", nameof(字节数));

            if (字节数 == 0)
                return "0 Bytes";

            string[] 单位 = { "Bytes", "KB", "MB", "GB", "TB", "PB", "EB", "ZB", "YB" };
            int 单位索引 = 0;
            double 大小 = 字节数;

            while (大小 >= 1024 && 单位索引 < 单位.Length - 1)
            {
                大小 /= 1024;
                单位索引++;
            }

            if (单位索引 == 0)
                return $"{大小} {单位[单位索引]}";
            else
                return $"{Math.Round(大小, 保留小数)} {单位[单位索引]}";
        }
    }
}
