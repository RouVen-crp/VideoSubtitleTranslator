# AGENTS.md

## 🔴 红线（最高优先级）

**绝对禁止在任何地方展示/输出/记录 API KEY。** 包括但不限于：
- 控制台输出、日志文件
- commit 消息、PR/Issue 正文、代码注释
- 任何 Markdown 文档（含 README、AGENTS.md）
- 任何配置文件中写入明文密钥（始终用环境变量引用）

即使 `ApiCaller.cs` 在缺少 Key 时会打印提示文本，也只输出**环境变量名**（如 `DEEPSEEK_API_KEY`），绝不输出变量值。

## 项目概览

- `.NET 10` 控制台应用（SDK 风格 `.csproj`，`net10.0`）
- 流水线架构：步骤为无状态的 `IPipelineStep` 实现，由 `pipeline.config.json` 驱动
- 步骤通过 `[PipelineStep]` 特性 + 反射（`PipelineRegistry`）自动发现
- 运行时配置：单例 `GlobalRuntimeConfig.Current`（在 `PipelineRunner.Run` 中设置）
- 外部工具：`yt-dlp`、`ffmpeg`、Python（Whisper + `srt`）
- 支持两种模式：`standard`（通用翻译）和 `meme`（短句/口语化 + 文化注释）

## 构建与运行

```bash
# 构建
dotnet build VideoSubtitleTranslator/VideoSubtitleTranslator.sln

# 运行（从仓库根目录）
dotnet run --project VideoSubtitleTranslator/VideoSubtitleTranslator -- --mode standard "<url>" "<workspace_dir>"
dotnet run --project VideoSubtitleTranslator/VideoSubtitleTranslator -- --mode meme "<url>" "<workspace_dir>"

# 运行（从项目目录 cd VideoSubtitleTranslator/VideoSubtitleTranslator）
dotnet run -- --mode standard "<url>" "<workspace_dir>"
```

## 环境准备

```bash
# 在 VideoSubtitleTranslator/VideoSubtitleTranslator 目录执行：
./scripts/init_python_venv.sh   # 创建 PythonScripts/venv 并安装 openai-whisper、srt
```

PATH 中需要：`yt-dlp`、`ffmpeg`、`python3`。

### API Key 配置

```bash
# 在 VideoSubtitleTranslator/VideoSubtitleTranslator 目录：
cp .env.example .env
# 编辑 .env，填入 DEEPSEEK_API_KEY
```

`.env` 由 `Program.cs:LoadEnv()` 自动加载（启动时）。`.gitignore` 已排除 `.env` 文件。LLM API Key 通过环境变量设置（默认变量名：`DEEPSEEK_API_KEY`，由 `LlmConfig.ApiKeyEnv` 定义）。

`.env.example` 中可选的 `LLM_BASE_URL` 和 `LLM_MODEL` **不会**被代码直接读取——这些值在 `Config/translator.config.json` 中配置。只有 `DEEPSEEK_API_KEY` 和 `COOKIES_FILE` 从环境变量读取。

### 运行测试

```bash
dotnet test VideoSubtitleTranslator.Tests/VideoSubtitleTranslator.Tests.csproj
```

## 架构要点

- **流水线上下文** 是使用 `StringComparer.OrdinalIgnoreCase` 比较器的 `Dictionary<string,string>`。步骤通过 `WorkspacePath`、`VideoExt`、`Url`、`VideoTitle` 等键读写数据。
- **添加步骤**：创建抽象基类并标记 `[PipelineStep("StepName")]`，再创建具体实现类并标记 `[PipelineStep("StepName", Implementation = "ImplName")]`。无需手动注册——`PipelineRegistry` 会扫描程序集。
- **RequiredKeys** 在 `[PipelineStep]` 特性中声明，会在步骤执行前校验。
- **提示词文件** 存放在 `Prompts/` 目录（Markdown 格式），含 `CulturalAnnotate/`、`Normalize/`、`Proofread/`、`Translate/`（含 `Meme/` 子目录）。通过 `PromptProvider.Get("Translate/user_prompt_template.md")` 读取——使用 `{{KEY}}` 模板语法，配合 `PipelineTextUtils.ApplyTemplate` 替换。
- **输出文件** 见 `WorkDirs.cs`。所有输出路径基于 `workspacePath`（工作区根目录下的按视频分子目录）。

## 配置文件

以下文件会复制到输出目录（构建时源位置必须存在）：

- `pipeline.config.json` —— standard 模式流水线（11 个步骤）
- `pipeline_meme.config.json` —— meme 模式流水线（增加 `CulturalAnnotate`/`DeepSeek` 和 `BuildDualSubtitle`/`Default`）
- `Config/translator.config.json` —— LLM、翻译、Whisper、烧录设置
- `Config/translator_meme.config.json` —— meme 模式翻译设置（关闭校对，小模块分段）

## 注意事项

- 任何步骤执行前 **必须** 已设置 `GlobalRuntimeConfig.Current`。通常由 `PipelineRunner.Run()` 在运行时设置；若手动调用步骤，需先自行设置。
- Python 虚拟环境 **不会** 在运行时自动创建——需先手动执行 `init_python_venv.sh`。
- `.gitignore` 使用 `[Bb]in/` / `[Oo]bj/` 字符类写法——Git 在跨平台下能正确处理此类模式。`.env` 文件已排除。
- `.csproj` 路径嵌套：`VideoSubtitleTranslator/VideoSubtitleTranslator/VideoSubtitleTranslator.csproj`。`.sln` 文件位于 `VideoSubtitleTranslator/VideoSubtitleTranslator.sln`。测试项目在根目录 `VideoSubtitleTranslator.Tests/`。
- 详细配置项说明及输出产物列表请参阅 `README.md`。

## Agent skills

### Issue tracker

GitHub Issues（`RouVen-crp/VideoSubtitleTranslator`），使用 `gh` CLI 操作。详见 `docs/agents/issue-tracker.md`。

### Triage labels

全部使用默认标签名（`needs-triage`、`needs-info`、`ready-for-agent`、`ready-for-human`、`wontfix`）。详见 `docs/agents/triage-labels.md`。

### Domain docs

单上下文布局（`CONTEXT.md` + `docs/adr/` 位于仓库根目录）。详见 `docs/agents/domain.md`。
