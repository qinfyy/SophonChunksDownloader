# SophonChunksDownloader

该工具用于下载 miHoYo 的游戏资源，这些资源使用分块压缩的方式进行传输。

## 环境要求

- [.NET 9](https://dotnet.microsoft.com/zh-cn/download/dotnet/9.0)

## 运行

1. 编译并运行项目
    ```shell
   dotnet build
   cd .\bin\Debug\net9.0
   .\SophonChunksDownloader.exe
   ```
2. 输入以下参数：
   - 清单下载地址前缀（Manifest URL Prefix）
   - 清单 ID（Manifest ID）
   - 文件下载地址前缀（Chunks URL Prefix）
   - 文件保存目录（Save Directory）