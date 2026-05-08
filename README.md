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

```bash
# 1. 配置环境变量
cp .env.example .env
# 编辑 .env 填入 DEEPSEEK_API_KEY

# 2. 初始化 Python 环境
cd VideoSubtitleTranslator
./scripts/init_python_venv.sh

# 3. 运行
dotnet run --project VideoSubtitleTranslator -- "<url>" "<workspace_dir>" --mode meme
```

## 初始化 Python 环境

在 `VideoSubtitleTranslator` 目录执行：

```bash
./scripts/init_python_venv.sh
```

该脚本会创建 `PythonScripts/venv` 并安装 `openai-whisper`、`srt`。

若系统 `python3` 与 Homebrew 存在兼容问题（如 `pyexpat` 符号缺失），可使用 `uv` 替代：

```bash
brew install uv
uv venv PythonScripts/venv --python 3.13
uv pip install --python PythonScripts/venv/bin/python3 -r PythonScripts/requirements.txt
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
dotnet run --project VideoSubtitleTranslator -- "<url>" "<workspace_dir>"
```

完整参数：

```
VideoSubtitleTranslator <url> <workspace_dir> [--mode <standard|meme>] [pipeline_config_json_path] [translator_config_json_path]
```

- `url`：视频链接。
- `workspace_dir`：工作根目录。
- `--mode`：可选，运行模式。`standard`（默认）或 `meme`（短视频口语化翻译+文化注释）。
- `pipeline_config_json_path`：可选，流程配置路径。
- `translator_config_json_path`：可选，运行配置路径。

### 示例

```bash
# 标准模式
dotnet run --project VideoSubtitleTranslator -- "https://www.youtube.com/watch?v=jNQXAC9IVRw" "/tmp/translates"

# Meme 模式（口语化翻译 + 文化注释）
dotnet run --project VideoSubtitleTranslator -- "https://www.youtube.com/watch?v=xxx" "/tmp/translates" --mode meme
```

### .env 环境变量

API Key 通过 `.env` 文件配置，避免硬编码：

```
DEEPSEEK_API_KEY=sk-your-key   # 必填
COOKIES_FILE=/path/to/cookies   # 可选，绕过 YouTube 反爬
LLM_BASE_URL=...                # 可选
LLM_MODEL=...                   # 可选
```

程序启动时自动加载项目目录下的 `.env` 文件。

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
