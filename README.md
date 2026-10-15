# VideoSubtitleTranslator

`VideoSubtitleTranslator` 用于将在线视频处理为中文字幕，并可按配置烧录字幕视频。

支持两种运行模式：

- **standard**：通用翻译模式，适合长视频、教程、访谈
- **meme**：短视频/meme 模式，口语化翻译 + 文化注释双重字幕（白字译文 + 灰字背景说明）

## 环境要求

- `.NET 10 SDK`
- `yt-dlp`
- `ffmpeg`
- `python3`
- 可用的 OpenAI-Compatible LLM API

## 快速开始

### Windows（PowerShell）

```powershell
cd VideoSubtitleTranslator
PowerShell -ExecutionPolicy Bypass -File .\scripts\setup_windows.ps1
$machinePath = [Environment]::GetEnvironmentVariable("Path", "Machine")
$userPath = [Environment]::GetEnvironmentVariable("Path", "User")
$env:Path = "$machinePath;$userPath;$env:Path"
.\PythonScripts\venv\Scripts\Activate.ps1
Copy-Item .env.example .env
# 编辑 .env，只填写所需环境变量；没有 cookies 文件时请注释 COOKIES_FILE
cd ..
dotnet run --project VideoSubtitleTranslator -- --mode meme "<url>"
```

`setup_windows.ps1` 会在缺少时通过 `winget` 安装 `yt-dlp` 和 `ffmpeg`，创建
`PythonScripts\venv`，并安装 `openai-whisper`、`srt`。如果安装了新的外部工具，
上述命令会刷新当前 PowerShell 的 `PATH`，使新安装的工具立即可用；也可以关闭并
重新打开 PowerShell，让 `PATH` 变更生效。

脚本会优先使用 Windows `py` 启动器，避免 Anaconda 环境中的 DLL 冲突。若需要
重建已存在的生成型虚拟环境，可追加 `-RecreateVenv`。

### Linux / macOS

```bash
# 1. 配置环境变量
cp .env.example .env
# 编辑 .env 填入 DEEPSEEK_API_KEY

# 2. 初始化 Python 环境
cd VideoSubtitleTranslator
./scripts/init_python_venv.sh

# 3. 运行
dotnet run --project VideoSubtitleTranslator -- "<url>" --mode meme
```

## 初始化 Python 环境

Linux / macOS 在 `VideoSubtitleTranslator` 目录执行：

```bash
./scripts/init_python_venv.sh
```

该脚本会创建 `PythonScripts/venv` 并安装 `openai-whisper`、`srt`。
脚本同时兼容 Unix 的 `venv/bin` 和 Windows 的 `venv/Scripts`；可通过
`PYTHON_CMD=/path/to/python ./scripts/init_python_venv.sh` 指定 Python。

在 Windows PowerShell 中也可以直接执行等价命令：

```powershell
py -3 -m venv PythonScripts/venv
& .\PythonScripts\venv\Scripts\python.exe -m pip install -r .\PythonScripts\requirements.txt
$env:VST_PYTHON_PATH = (Resolve-Path .\PythonScripts\venv\Scripts\python.exe)
```

主机端不会自动安装 `ffmpeg`、`ffprobe`、`yt-dlp` 或 Python；请先安装这些工具并通过
`VST_FFMPEG_PATH`、`VST_FFPROBE_PATH`、`VST_YTDLP_PATH`、`VST_PYTHON_PATH` 配置路径。

若系统 `python3` 与 Homebrew 存在兼容问题（如 `pyexpat` 符号缺失），可使用 `uv` 替代：

```bash
brew install uv
uv venv PythonScripts/venv --python 3.13
uv pip install --python PythonScripts/venv/bin/python3 -r PythonScripts/requirements.txt
```

### 手动下载 Whisper turbo 模型（Windows）

网络较慢或自动下载失败时，可以通过 ModelScope 单独下载 `turbo` 模型。在
`VideoSubtitleTranslator` 项目目录中执行：

```powershell
# 1. 启动虚拟环境
.\PythonScripts\venv\Scripts\Activate.ps1

# 2. 安装 ModelScope
python -m pip install modelscope

# 3. 只下载模型 checkpoint
modelscope download --model iic/Whisper-large-v3-turbo/large-v3-turbo.pt --local_dir 'D:\CODE\myApp\VideoSubtitleTranslator\VideoSubtitleTranslator\PythonScripts\models\whisper'
```

如果仓库不在上述位置，请将 `--local_dir` 替换为当前项目的
`PythonScripts\models\whisper` 绝对路径。下载完成后应存在以下单个 checkpoint
文件：

```text
PythonScripts\models\whisper\large-v3-turbo.pt
```

该目录已被 Git 忽略，不会提交约 1.5 GB 的模型文件。可用以下命令校验下载结果：

```powershell
Get-FileHash .\PythonScripts\models\whisper\large-v3-turbo.pt -Algorithm SHA256
```

预期 SHA-256：

```text
aff26ae408abcba5fbf8813c21e62b0941638c5f6eebfb145be0c9839262a19a
```

## 配置文件

- 流程配置：`VideoSubtitleTranslator/pipeline.config.json`
- 运行配置：`VideoSubtitleTranslator/Config/translator.config.json`

### pipeline.config.json 写法

`steps` 是按顺序执行的步骤列表，每项包含：

- `step`：步骤名
- `implementation`：该步骤对应实现名

默认示例：

```json
{
  "steps": [
    { "step": "SecurityFilter", "implementation": "Default" },
    { "step": "InitMetadata", "implementation": "MetaGetter" },
    { "step": "InitWorkspace", "implementation": "Default" },
    { "step": "Download", "implementation": "Default" },
    { "step": "SplitAudio", "implementation": "Default" },
    { "step": "ExtractSubtitle", "implementation": "Default" },
    { "step": "BuildSentences", "implementation": "Default" },
    { "step": "NormalizeSubtitle", "implementation": "DeepSeek" },
    { "step": "Translate", "implementation": "DeepSeek" },
    { "step": "ProofreadSubtitle", "implementation": "DeepSeek" },
    { "step": "BurnSubtitle", "implementation": "Default" }
  ]
}
```

当前内置可用步骤与实现：

- `SecurityFilter`: `Default`
- `InitMetadata`: `MetaGetter`
- `InitWorkspace`: `Default`
- `Download`: `Default`
- `SplitAudio`: `Default`
- `ExtractSubtitle`: `Default`
- `BuildSentences`: `Default`
- `NormalizeSubtitle`: `DeepSeek`
- `Translate`: `DeepSeek` / `Meme`
- `CulturalAnnotate`: `DeepSeek`（meme 模式文化注释）
- `BuildDualSubtitle`: `Default`（meme 模式 ASS 双轨字幕生成）
- `ProofreadSubtitle`: `DeepSeek`
- `BurnSubtitle`: `Default`

### translator.config.json 配置项

#### llm

- `baseUrl`：聊天补全接口地址。
- `apiKeyEnv`：API Key 对应的环境变量名。
- `model`：模型名。
- `retryCount`：请求失败后的重试次数。
- `timeoutSeconds`：单次请求超时秒数。
- `temperature`：采样温度。
- `maxTokens`：单次响应最大 token 限额。

#### translation

- `domainHintPrompt`：翻译域提示词。
- `metaRulesMaxCount`：元翻译规则最大保留条数。
- `metaRuleMaxChars`：单条元翻译规则最大字符数。
- `moduleSentenceCount`：每个翻译模块包含的句子数。
- `contextModuleWindow`：翻译时的上下文窗口模块数。
- `requestDelayMs`：翻译模块请求间隔毫秒数。
- `moduleJsonMaxAttempts`：翻译模块 JSON 解析最大尝试次数。
- `synopsisMaxChars`：前情提要最大字符数。
- `normalizeJsonMaxAttempts`：预处理 JSON 解析最大尝试次数。
- `normalizeWindowSize`：字幕预处理窗口大小。
- `synopsisCompressEveryModules`：每处理 N 个模块进行一次前情提要压缩。
- `proofreadEnabled`：是否启用翻译后校对。
- `proofreadJsonMaxAttempts`：校对 JSON 解析最大尝试次数。
- `proofreadWindowSize`：校对窗口大小。
- `proofreadRequestDelayMs`：校对请求间隔毫秒数；`0` 表示沿用 `requestDelayMs`。
- `culturalAnnotateEnabled`：是否启用文化注释步骤（meme 模式下自动开启）。

#### 全局

- `cookiesFromBrowser`：yt-dlp cookies 来源浏览器名（如 `"chrome"`），为空表示不传 cookie。
  - 也可通过 `.env` 中 `COOKIES_FILE` 环境变量指定 cookies.txt 文件路径。优先级：`COOKIES_FILE` > `cookiesFromBrowser`。

#### whisper

- `pythonExecutable`：用于调用 Whisper 的 Python 可执行文件。
- `model`：Whisper 模型名。
- `language`：识别语言代码。
- `force`：是否强制重新生成字幕。

#### burn

- `enabled`：是否执行字幕烧录。
- `videoEncoder`：烧录视频编码器。
- `qualityPreset`：烧录质量档位，支持：
  - `auto`：使用 ffmpeg 默认质量参数。
  - `high`：使用高质量预设参数。
- `enforceSourceBitrateFloor`：是否读取原视频流码率并保证输出视频码率不低于原视频。
- `minOutputBitrateKbps`：额外最小输出码率（kbps）；实际输出码率下限为 `max(原视频码率, minOutputBitrateKbps)`。

## 启动方式

### 基本用法

```bash
dotnet run --project VideoSubtitleTranslator -- "<url>"
```

完整参数：

```
VideoSubtitleTranslator <url> [workspace_dir] [--mode <standard|meme>] [pipeline_config_json_path] [translator_config_json_path]
```

- `url`：视频链接。
- `workspace_dir`：可选，工作根目录；不传时默认为项目目录下的 `output/`。
- `--mode`：可选，运行模式。`standard`（默认）或 `meme`（短视频口语化翻译+文化注释）。
- `pipeline_config_json_path`：可选，流程配置路径。
- `translator_config_json_path`：可选，运行配置路径。

### 示例

```bash
# 标准模式
dotnet run --project VideoSubtitleTranslator -- "https://www.youtube.com/watch?v=jNQXAC9IVRw"

# Meme 模式（口语化翻译 + 文化注释）
dotnet run --project VideoSubtitleTranslator -- "https://www.youtube.com/watch?v=xxx" --mode meme

# 自定义输出目录（可选）
dotnet run --project VideoSubtitleTranslator -- "https://www.youtube.com/watch?v=xxx" "D:\VideoTranslations"
```

### .env 环境变量

API Key 通过 `.env` 文件配置，避免硬编码：

```
DEEPSEEK_API_KEY=sk-your-key   # 必填
COOKIES_FILE=/path/to/cookies   # 可选，绕过 YouTube 反爬
LLM_BASE_URL=...                # 可选
LLM_MODEL=...                   # 可选
VST_FFMPEG_PATH=C:\Tools\ffmpeg.exe   # Windows 可选；覆盖 PATH 中的 ffmpeg
VST_FFPROBE_PATH=C:\Tools\ffprobe.exe # 可选；缺失时 Web 校验会回退到 ffmpeg
VST_YTDLP_PATH=C:\Tools\yt-dlp.exe    # Windows 可选；覆盖 PATH 中的 yt-dlp
VST_PYTHON_PATH=C:\path\to\python.exe # Windows 可选；用于 Whisper 字幕提取
VST_WHISPER_MODEL_PATH=C:\path\to\ggml-large-v3-turbo.bin # 可选；FFmpeg whisper.cpp fallback
VST_WHISPER_USE_GPU=false             # 可选；FFmpeg whisper.cpp 使用 GPU
```

程序启动时自动加载项目目录下的 `.env` 文件。

程序不会自动在宿主机安装 `ffmpeg`、`ffprobe`、`yt-dlp` 或 Whisper/Python 依赖。CLI 默认按 PATH 查找外部工具；Windows 可以通过上面的环境变量提供绝对路径。若 Python/Torch 在 Windows 上无法加载，可使用带 `whisper.cpp` 滤镜的 FFmpeg，并通过 `VST_WHISPER_MODEL_PATH` 指向 `ggml-*.bin` 模型；需要 GPU 时再设置 `VST_WHISPER_USE_GPU=true`。Docker 镜像会安装 `ffmpeg` 和 Python 依赖，但 Web 首版只接收本地上传视频，不需要 `yt-dlp`；使用 CLI 的 YouTube 下载流程时仍需提供 `yt-dlp`。

## 输出产物

### 通用产物

- `video.<ext>`：下载后原视频。
- `thumbnail.png`：视频封面。
- `audio.wav`：抽取出的音频。
- `subtitle.srt`：英文字幕。
- `raw_subtitle.txt`：英文逐句文本。
- `subtitle.normalized.srt`：规范化英文字幕。
- `raw_subtitle.normalized.txt`：规范化英文逐句文本。
- `subtitle_normalize.log`：预处理日志。
- `translated_subtitle.srt`：中文字幕结果。
- `translated_subtitle.proofread.srt`：校对后中文字幕。
- `raw_translated_subtitle.txt`：中文逐句文本。
- `term_consistency_table.txt`：术语表。
- `meta_translation_rules.txt`：元翻译规则表。
- `synopsis_memory.txt`：前情提要记忆。
- `ad_memory.txt`：广告记忆。
- `translator_prompt_snapshot.md`：提示词快照。
- `prompt_history.log`：翻译请求与响应日志。
- `memory_history.log`：记忆状态日志。
- `subtitle_proofread.log`：校对日志。
- `info.txt`：视频元信息。

### Meme 模式专属产物

- `cultural_annotations.json`：文化注释数据。
- `cultural_term_table.txt`：文化术语记忆。
- `cultural_context.txt`：文化背景上下文记忆。
- `dual_subtitle.ass`：ASS 格式双轨字幕（白字译文 + 灰字注释）。
- `<中文标题>.mp4`：烧录双轨字幕后的最终视频。

## 本地 Web 工作台

首版 Web 工作台运行在本机回环地址，默认通过 SQLite 保存任务、字幕修订和导出记录，并把每个任务的输入与阶段产物放在独立目录中：

```bash
dotnet restore VideoSubtitleTranslator/VideoSubtitleTranslator.csproj
dotnet run --project VideoSubtitleTranslator -- --web
# 浏览器打开 http://127.0.0.1:5155
```

也可以使用 Windows Docker Desktop：

```bash
copy VideoSubtitleTranslator/.env.example .env
# 在 .env 设置服务端 DEEPSEEK_API_KEY
docker compose up --build
```

Web 首版只接受可播放的 H.264/AAC MP4，最大 500 MiB、最长 10 分钟。服务绑定到 `127.0.0.1`，可通过 `VST_BIND_ADDRESS`、`VST_WEB_PORT` 和 `VST_DATA_DIR` 调整。未设置 `VST_DATA_DIR` 时，任务和导出会保存到项目根目录的 `outputs/`，不会默认写入运行时的 C 盘 `bin` 目录。Whisper 模型继续使用现有 Python/Whisper 缓存；首次使用某模型会产生下载和本地缓存时间。Web 页面只接收状态和字幕内容，API Key 始终留在服务端。媒体校验优先使用 `ffprobe`；只有 `ffmpeg.exe` 时会使用 `ffmpeg -i` 的媒体信息作为回退。

本地页面源码位于 `WebClient/`，使用 Vue 3、TypeScript 和 Vite；Docker 构建时会先生成静态资源，再由 ASP.NET Core 提供给工作台。

任务在后台单实例队列中执行。刷新或关闭页面不会停止任务；服务重启会把执行中的任务标为“中断”，由用户确认可能的重复 API 费用后恢复。导出会固定引用某个字幕 revision，后续编辑只会把旧导出标记为过期，不会覆盖历史文件。

### 实时查看本地进度

启动工作台后，打开 `http://127.0.0.1:5155`。任务列表和选中任务会每 1.5 秒自动刷新，显示当前阶段、已处理条目数、累计已用时间；尚未开始的工作还会显示真实的队列位置。页面不会猜测百分比、剩余时间或 ETA。浏览器可以关闭再打开，任务会继续由本机服务执行。

## 提示词维护

固定提示词已统一放在 `VideoSubtitleTranslator/Prompts/` 目录，运行时直接读取 Markdown 提示词文件（`*.md`）：

- `Prompts/Translate/`：翻译主流程提示词（含模块翻译、标题翻译、缺句修复）。
- `Prompts/Translate/Meme/`：Meme 模式翻译提示词（口语化、俚语容忍）。
- `Prompts/Normalize/`：字幕预处理（句子合并判定）提示词。
- `Prompts/Proofread/`：翻译后字幕校对提示词。
- `Prompts/CulturalAnnotate/`：Meme 模式文化注释提示词（判定是否需要注释+生成背景说明）。
- 提示词文本采用结构化 Markdown 组织（任务、输入上下文、输出契约、硬约束），便于维护与审阅。

## Normalize / Proofread 行为说明

- `NormalizeSubtitle` 采用滚动窗口状态转移：
  - 不合并：输出左句，右句晋升为左句，下一句进入右句。
  - 合并：左右句在业务侧合并为新左句，下一句进入右句。
- `NormalizeSubtitle` 中 LLM 只负责输出 `merge=true/false` 判定，不再返回合并后的句子文本。
- `ProofreadSubtitle` 采用同样的左值滚动机制处理相邻两条字幕窗口。
- `subtitle_normalize.log` 与 `subtitle_proofread.log` 默认仅记录关键事件（如合并/拒绝合并）和结束统计，减少噪声日志。

## 测试

### 运行测试

```bash
dotnet test VideoSubtitleTranslator.sln
```

### 测试覆盖

测试项目 `VideoSubtitleTranslator.Tests`（xUnit），37 个测试覆盖以下行为：

| 测试类 | 覆盖内容 |
|--------|---------|
| `TranslatorRuntimeConfigTests` | JSON 配置反序列化（含 `culturalAnnotateEnabled`） |
| `WorkDirsTests` | 文件路径生成（含 meme 专属路径） |
| `ArgumentParsingTests` | `--mode` 参数解析、默认值、边界情况 |
| `ConfigPathResolutionTests` | 模式切换时的配置路径选择 |
| `MemeTranslateStepRegistrationTests` | `PipelineRegistry` 解析 `Translate/Meme` |
| `MemePromptLoadingTests` | Meme 提示词文件存在且与标准提示词不同 |
| `CulturalAnnotateStepRegistrationTests` | `PipelineRegistry` 解析 `CulturalAnnotate/DeepSeek` |
| `CulturalAnnotatePromptTests` | 文化注释提示词存在 |
| `BuildDualSubtitleTests` | SRT 解析、ASS 生成、字符转义、无注释场景 |
| `BurnSubtitleDualTrackTests` | ASS/SRT 路径选择、ffmpeg 参数构建、回退逻辑 |
| `CookiesConfigTests` | `cookiesFromBrowser` JSON 反序列化 |
| `GitignoreTests` | `.env` 已加入 `.gitignore` |
| `ApiKeyPromptTests` | API Key 缺失时提示用户创建 `.env` |

### 测试哲学

所有测试验证**公共接口行为**而非内部实现。配置反序列化测试验证 JSON 字段正确映射；流水线注册测试验证步骤可被 `PipelineRegistry` 发现；路径生成测试验证输出文件位置正确。不 mock 内部协作者，不测试私有方法。测试可在不改变行为的前提下承受内部重构。
