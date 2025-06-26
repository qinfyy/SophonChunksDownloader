namespace SophonChunksDownloader
{
    public class ManifestConfig
    {
        public int retcode { get; set; }
        public string message { get; set; }
        public ManifestData data { get; set; }
    }

    public class ManifestData
    {
        public string build_id { get; set; }
        public string tag { get; set; }
        public List<ManifestCategory> manifests { get; set; }
    }

    public class ManifestCategory
    {
        public string category_id { get; set; }
        public string category_name { get; set; }
        public ManifestDetail manifest { get; set; }
        public DownloadConfig chunk_download { get; set; }
        public DownloadConfig manifest_download { get; set; }
        public string matching_field { get; set; }
        public FileStats stats { get; set; }
        public FileStats deduplicated_stats { get; set; }
    }

    public class ManifestDetail
    {
        public string id { get; set; }
        public string checksum { get; set; }
        public string compressed_size { get; set; }
        public string uncompressed_size { get; set; }
    }

    public class DownloadConfig
    {
        public int encryption { get; set; }
        public string password { get; set; }
        public int compression { get; set; }
        public string url_prefix { get; set; }
        public string url_suffix { get; set; }
    }

    public class FileStats
    {
        public string compressed_size { get; set; }
        public string uncompressed_size { get; set; }
        public string file_count { get; set; }
        public string chunk_count { get; set; }
    }
}
