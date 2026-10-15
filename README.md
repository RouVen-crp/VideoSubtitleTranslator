# VideoSubtitleTranslator

**在线视频字幕翻译与本地校对工具。** 下载或导入英文视频，生成中文字幕，在浏览器中逐句校对，再导出字幕或烧录字幕的视频。

<p align="center">
  <a href="https://rouven-crp.github.io/VideoSubtitleTranslator/">在线体验静态演示</a> ·
  <a href="#本地-web-工作台图形界面">启动本地图形界面</a> ·
  <a href="#命令行用法">命令行用法</a>
</p>

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

## 快速开始

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

### macOS / Linux

先安装 .NET 10、Python 3、`ffmpeg` 和 `yt-dlp`，然后在仓库根目录运行：

```bash
# 创建本机配置文件，并编辑 VideoSubtitleTranslator/.env
cp VideoSubtitleTranslator/.env.example VideoSubtitleTranslator/.env

# 创建 Python 虚拟环境并安装 Whisper 依赖
cd VideoSubtitleTranslator
./scripts/init_python_venv.sh
cd ..

# 启动本地浏览器工作台
dotnet run --project VideoSubtitleTranslator -- --web
```

打开 <http://127.0.0.1:5155>。`.env` 至少需要配置 `DEEPSEEK_API_KEY`。没有 cookies 文件时，请删除或注释 `COOKIES_FILE` 示例行。

`init_python_venv.sh` 会在 `VideoSubtitleTranslator/PythonScripts/venv` 创建虚拟环境并安装 Whisper 依赖。可通过 `PYTHON_CMD=/path/to/python` 指定 Python 可执行文件。

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

命令行从视频链接开始下载和处理。先按上面的步骤准备依赖和 `.env`，然后从仓库根目录运行：

```powershell
# 通用翻译模式（默认）
dotnet run --project VideoSubtitleTranslator -- "<视频链接>"

# Meme 模式：口语化翻译并生成文化注释
dotnet run --project VideoSubtitleTranslator -- "<视频链接>" --mode meme

# 指定输出根目录
dotnet run --project VideoSubtitleTranslator -- "<视频链接>" "D:\VideoTranslations" --mode standard
```

macOS/Linux 命令相同，只需将 PowerShell 示例中的 Windows 输出路径换成 Unix 路径。

完整参数格式：

```text
VideoSubtitleTranslator <url> [workspace_dir] [--mode <standard|meme>] [pipeline_config_json_path] [translator_config_json_path]
```

- `url`：必填，公开可访问的视频链接。
- `workspace_dir`：可选，工作区根目录；省略时使用 `VideoSubtitleTranslator/output/`。
- `--mode`：可选，`standard` 为默认模式，`meme` 启用口语化翻译和文化注释。
- `pipeline_config_json_path`、`translator_config_json_path`：可选的自定义流水线和翻译配置路径。

CLI 使用 `yt-dlp` 下载视频；如果网站要求登录，需按 yt-dlp 支持的方式配置 cookies。标准模式和 Meme 模式分别读取 `pipeline.config.json` / `Config/translator.config.json` 与 `pipeline_meme.config.json` / `Config/translator_meme.config.json`。

## 配置

运行配置位于 `VideoSubtitleTranslator/Config/`：

- `translator.config.json`：LLM、Whisper、字幕校对和视频烧录设置。
- `translator_meme.config.json`：Meme 模式配置，默认启用文化注释并关闭翻译后校对。
- `pipeline.config.json`、`pipeline_meme.config.json`：各模式的流水线步骤和实现。

`.env` 通过环境变量提供本机密钥和可选路径；该文件已加入 `.gitignore`。配置 API Key 时只需设置 `DEEPSEEK_API_KEY`，配置项 `apiKeyEnv` 指向此环境变量。`COOKIES_FILE` 可选，用于将 cookies 文件路径提供给 yt-dlp；没有该文件时请移除示例配置。

可通过 `VST_FFMPEG_PATH`、`VST_FFPROBE_PATH`、`VST_YTDLP_PATH`、`VST_PYTHON_PATH` 指定外部工具的可执行文件路径。Whisper 默认使用 `turbo` 模型；首次运行可能需要下载模型并等待缓存。

## 输出文件

默认情况下，CLI 将任务文件写入项目目录 `VideoSubtitleTranslator/output/` 下按视频区分的子目录。Web 工作台将数据库、任务输入和导出写入仓库根目录 `outputs/`，也可用 `VST_DATA_DIR` 修改。

根据模式和配置，常见产物包括：

| 产物 | 说明 |
| --- | --- |
| `subtitle.srt` | 识别出的原文字幕 |
| `subtitle.normalized.srt` | 规范化后的原文字幕 |
| `translated_subtitle.srt` | 中文字幕 |
| `translated_subtitle.proofread.srt` | 校对后的中文字幕 |
| `dual_subtitle.ass` | Meme 模式的双轨字幕（译文与文化注释） |
| `<中文标题>.mp4` | 开启烧录后生成的字幕视频 |
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
