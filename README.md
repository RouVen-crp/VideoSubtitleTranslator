# VideoSubtitleTranslator

**轻量命令行视频字幕翻译工具。** 用简短命令下载视频、生成中文字幕，或播放已完成的任务。

<p align="center">
  <a href="#快速开始">快速开始</a> ·
  <a href="#依赖检查">依赖检查</a> ·
  <a href="https://rouven-crp.github.io/VideoSubtitleTranslator/">在线演示</a>
</p>

## 快速开始

开发环境需要 .NET 10 SDK。按任务运行对应的依赖检查；已安装在系统中的工具可以直接复用，无需重新安装。需要覆盖默认查找位置时，可通过 `VST_FFMPEG_PATH`、`VST_FFPROBE_PATH`、`VST_YTDLP_PATH`、`VST_PYTHON_PATH` 指定可执行文件。

先检查实际任务需要的依赖：

```sh
zimu doctor download   # 只检查下载器
zimu doctor translate  # 检查翻译配置、媒体工具、Python/Whisper 和 LLM 环境变量
```

macOS 可用 Homebrew 安装缺少的系统工具：

```sh
brew install ffmpeg yt-dlp
```

翻译还需要 Whisper Python 依赖和 LLM 凭据。若机器已有可用的 Whisper Python 环境，可用 `VST_PYTHON_PATH` 指向它；否则从仓库根目录运行：

```sh
cd VideoSubtitleTranslator
./scripts/init_python_venv.sh
cd ..
cp VideoSubtitleTranslator/.env.example VideoSubtitleTranslator/.env
```

在本地 `.env` 或 shell 中设置 `DEEPSEEK_API_KEY`。不要把凭据写入命令历史或提交到仓库。安装包或开发环境提供 `zimu` 和短名 `zm` 两个入口；在仓库根目录开发运行时，可以使用 `dotnet run --project VideoSubtitleTranslator -- ...`：

```sh
zimu translate <视频链接或本地路径>
zimu download <视频链接>
zm translate <视频链接或本地路径>
```

单独运行 `zimu` 或 `zm` 会打开命令行引导界面。`translate` 需要 FFmpeg/ffprobe、Python/Whisper、翻译配置和 LLM 凭据；URL 输入还会下载视频，因此也需要 `yt-dlp` 和网络。本地视频翻译不需要 `yt-dlp`。`download` 只需要翻译配置、`yt-dlp` 和网络（需要登录的网站可配置 cookies），不需要 Python、Whisper、FFmpeg 或 LLM 凭据。

## 功能

- **本地 Web 工作台**：在浏览器中上传本地 MP4、查看任务进度、编辑双语字幕并导出视频。
- **命令行流水线**：从在线视频链接开始处理，适合批处理和自动化。
- **两种翻译模式**：`standard` 提供通用翻译与校对；`meme` 提供更口语化的翻译和文化注释。
- **可恢复的任务记录**：工作台保留字幕修订和导出记录；关闭页面不会中止后台任务。
- **静态在线演示**：无需安装，直接试用样例视频的播放、字幕编辑和 SRT 下载。

## 选择运行方式

| 方式 | 输入 | 适合场景 |
| --- | --- | --- |
| [本地 Web 工作台](#本地-web-工作台图形界面) | 本地 MP4 文件 | 希望用图形界面上传、校对和导出 |
| [命令行](#命令行用法) | 视频链接 | 处理在线视频或自动化流水线 |
| [GitHub Pages 演示](https://rouven-crp.github.io/VideoSubtitleTranslator/) | 固定样例 | 先体验播放器和字幕编辑，不运行翻译 |

## 环境要求

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Python 3
- `ffmpeg`（包含 `ffprobe`）
- `yt-dlp`（仅命令行在线视频下载需要）
- 可访问的 OpenAI-Compatible LLM 服务及对应 API Key

Windows 可运行仓库提供的安装脚本准备外部工具和 Python 依赖。macOS/Linux 请使用各自的软件包管理器安装 `ffmpeg`、`yt-dlp` 和 Python 3。

## Windows 快速开始

以下命令都从仓库根目录运行。

### Windows（PowerShell）

```powershell
# 安装 yt-dlp、ffmpeg，并创建 Python 虚拟环境、安装 Whisper 依赖
.\VideoSubtitleTranslator\scripts\setup_windows.ps1

# 创建本机配置文件，然后在编辑器中设置所需环境变量
Copy-Item .\VideoSubtitleTranslator\.env.example .\VideoSubtitleTranslator\.env

# 启动本地浏览器工作台
dotnet run --project VideoSubtitleTranslator -- --web
```

打开 <http://127.0.0.1:5155>。`.env` 至少需要配置 `DEEPSEEK_API_KEY`。若没有使用 cookies 文件，请从 `.env` 中删除或注释 `COOKIES_FILE` 示例行。

安装脚本通过 `winget` 安装缺失的 `yt-dlp` 和 `ffmpeg`，并安装 `openai-whisper`、`srt`。如果刚安装了外部工具，请重新打开 PowerShell 后再运行应用，让新 `PATH` 生效。脚本默认使用 Windows `py` 启动器；需要重建虚拟环境时可追加 `-RecreateVenv`。

## 本地 Web 工作台（图形界面）

工作台由本机 .NET 服务提供页面和 API，数据默认保存在仓库根目录的 `outputs/`。启动后在浏览器打开 <http://127.0.0.1:5155>，可以：

1. 上传 H.264/AAC 编码的 MP4。
2. 查看任务队列、当前阶段、已处理条目数和累计用时。
3. 播放视频并逐句修改英文原文或中文译文，也可以单独重译一句。
4. 保存字幕修订，导出中文字幕或中英双语视频与 SRT。

工作台首版限制：视频最大 500 MiB、最长 10 分钟。服务默认只监听本机地址 `127.0.0.1`。任务在后台继续运行，关闭浏览器不会中断任务；服务重启后，未完成的任务会标为中断。恢复任务可能再次调用 LLM 并产生费用，页面会提示用户确认。

需要自定义监听地址、端口或数据目录时，在启动服务前设置：

| 环境变量 | 默认值 | 作用 |
| --- | --- | --- |
| `VST_BIND_ADDRESS` | `127.0.0.1` | Web 服务绑定地址 |
| `VST_WEB_PORT` | `5155` | Web 服务端口 |
| `VST_DATA_DIR` | 仓库根目录下的 `outputs/` | 任务、数据库和导出文件目录 |

使用 Docker Desktop 也可启动工作台：

```powershell
Copy-Item VideoSubtitleTranslator/.env.example .env
# 在 .env 中设置服务端使用的 DEEPSEEK_API_KEY
docker compose up --build
```

## 命令行用法

命令行可处理在线视频或本地视频。先按“快速开始”准备对应任务所需的依赖，然后从仓库根目录运行：

```powershell
# 通用翻译模式（默认）
dotnet run --project VideoSubtitleTranslator -- translate "<视频链接或本地视频路径>"

# Meme 模式：口语化翻译并生成文化注释
dotnet run --project VideoSubtitleTranslator -- translate "<视频链接或本地视频路径>" --mode meme

# 指定输出根目录
dotnet run --project VideoSubtitleTranslator -- translate "<视频链接或本地视频路径>" --workspace "D:\VideoTranslations"

# 可选：生成双语 SRT 或烧录中文字幕视频
dotnet run --project VideoSubtitleTranslator -- translate "<视频链接或本地视频路径>" --bilingual --burn

# 单独下载在线视频
dotnet run --project VideoSubtitleTranslator -- download "<视频链接>"

# 检查下载或翻译依赖
dotnet run --project VideoSubtitleTranslator -- doctor download
dotnet run --project VideoSubtitleTranslator -- doctor translate
```

macOS/Linux 命令相同，只需将 PowerShell 示例中的 Windows 输出路径换成 Unix 路径。

发布命令为 `zimu`，也可用短名 `zm`。完整参数格式：

```text
zimu translate <URL|视频路径> [--mode standard|meme] [--bilingual] [--burn] [--workspace <目录>]
```

- `URL|视频路径`：必填，HTTP(S) 视频链接或本地视频文件路径。
- `--mode`：可选，`standard` 为默认模式，`meme` 启用口语化翻译和文化注释。
- `--bilingual`：可选，生成英中双语 SRT。
- `--burn`：可选，生成烧录字幕的视频；默认只生成字幕文件。
- `--workspace`：可选，工作区根目录；省略时使用 `VideoSubtitleTranslator/output/`。

CLI 使用 `yt-dlp` 下载视频；如果网站要求登录，需按 yt-dlp 支持的方式配置 cookies。标准模式和 Meme 模式分别读取 `pipeline.config.json` / `Config/translator.config.json` 与 `pipeline_meme.config.json` / `Config/translator_meme.config.json`。

## 配置

运行配置位于 `VideoSubtitleTranslator/Config/`：

- `translator.config.json`：LLM、Whisper、字幕校对和视频烧录设置。
- `translator_meme.config.json`：Meme 模式配置，默认启用文化注释并关闭翻译后校对。
- `pipeline.config.json`、`pipeline_meme.config.json`：各模式的流水线步骤和实现。

`.env` 通过环境变量提供本机密钥和可选路径；该文件已加入 `.gitignore`。配置 API Key 时只需设置 `DEEPSEEK_API_KEY`，配置项 `apiKeyEnv` 指向此环境变量。`COOKIES_FILE` 可选，用于将 cookies 文件路径提供给 yt-dlp；没有该文件时请移除示例配置。

可通过 `VST_FFMPEG_PATH`、`VST_FFPROBE_PATH`、`VST_YTDLP_PATH`、`VST_PYTHON_PATH` 指定外部工具的可执行文件路径。Whisper 默认使用 `turbo` 模型；首次运行可能需要下载模型并等待缓存。

## 依赖检查

运行 `zimu doctor download` 检查下载配置和 `yt-dlp`；运行 `zimu doctor translate` 检查翻译配置、媒体工具、Python/Whisper 和 LLM 所需环境变量。在线翻译还需要 `yt-dlp`，可用 `zimu doctor download` 单独检查；本地视频翻译不需要下载器。诊断只报告环境变量名称和是否已设置，不显示任何凭据值。翻译依赖 `ffmpeg` 与 `ffprobe`；若其中缺失，请安装 FFmpeg 或设置相应的 `VST_*_PATH`。Python 可用但 Whisper 或 `srt` 缺失时，运行 `VideoSubtitleTranslator/scripts/init_python_venv.sh`，或在指定的 Python 环境中安装 `PythonScripts/requirements.txt`。

### macOS 性能测量

目前没有公布 Whisper 总耗时承诺。要为特定机器测量端到端时间，可记录芯片型号、macOS 版本、视频时长与格式、Whisper 模型、模型是否已缓存，以及命令开始和结束时间。连续测量时分别记录首次运行和缓存后的运行，并使用相同视频与设置；不要把下载、模型首次下载或网络等待时间混同为 Whisper 推理时间。

## 输出文件

默认情况下，CLI 将任务文件写入项目目录 `VideoSubtitleTranslator/output/` 下按视频区分的子目录。Web 工作台将数据库、任务输入和导出写入仓库根目录 `outputs/`，也可用 `VST_DATA_DIR` 修改。

根据模式和配置，常见产物包括：

| 产物 | 说明 |
| --- | --- |
| `subtitle.srt` | 识别出的原文字幕 |
| `subtitle.normalized.srt` | 规范化后的原文字幕 |
| `translated_subtitle.srt` | 中文字幕 |
| `translated_subtitle.proofread.srt` | 校对后的中文字幕 |
| `translated_bilingual.srt` | 使用 `--bilingual` 时生成的英中双语字幕 |
| `dual_subtitle.ass` | Meme 模式的双轨字幕（译文与文化注释） |
| `burned_video.mp4` | 使用 `--burn` 时生成的烧录视频；若已存在则使用编号文件名 |
| `info.txt`、`thumbnail.png` | 视频元信息和封面（视源视频而定） |

## GitHub Pages 静态演示

[打开在线演示](https://rouven-crp.github.io/VideoSubtitleTranslator/) 可播放固定样例、跳转字幕时间点、切换中文或双语预览、编辑译文并下载 SRT。演示只在访问者的浏览器中处理固定样例，不上传媒体、不调用 AI，也不会生成新视频。

Pages 演示与本地 Web 工作台是两个不同入口：要处理自己的媒体并生成视频，请在本机运行工作台或 CLI。

## 开发

从仓库根目录还原并构建解决方案：

```bash
dotnet restore VideoSubtitleTranslator.sln
dotnet build VideoSubtitleTranslator.sln
```

运行测试：

```bash
dotnet test VideoSubtitleTranslator.sln
node --test tests/pages-demo.test.mjs
```

本地工作台前端源码位于 `WebClient/`（Vue 3、TypeScript、Vite）；`VideoSubtitleTranslator/Web/` 是供 .NET 服务托管的生成文件。GitHub Pages 静态演示独立位于 `docs/`。
