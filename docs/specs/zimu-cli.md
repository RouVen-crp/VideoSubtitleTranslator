# zimu：英文视频字幕翻译与播放的 CLI 工作流

## Problem Statement

用户想尽快把在线视频或本地英文视频变成可播放的中文字幕。当前仓库虽然已有翻译流水线，但命令行主要接受 URL，启动方式和参数较重；本地视频、持久任务、恢复和任务队列主要依赖 Web 工作台。翻译完成后，用户也无法直接从 CLI 打开带中文字幕的视频。安装、错误提示和长任务进度还不足以支撑一个易记、可信、适合日常使用的轻量命令行工具。

## Solution

将面向用户的产品命名为 **zimu**，提供 `zimu` 与 `zm` 两个等效启动命令。无参数启动时进入带底部操作提示的终端引导界面；熟悉命令的用户可直接使用 `translate`、`download`、`play` 和任务相关短命令。`translate` 接受 URL 或本地视频，默认从英文音频转录并生成中文字幕 SRT，保留现有 `standard` 和 `meme` 模式。用户可在界面中逐项加入多个任务，查看真实进度和历史，并在中断后恢复。字幕完成后由用户选择播放；播放优先使用已安装且能明确加载外置 SRT 的播放器，并提供默认播放器回退。视频烧录与双语 SRT 为可选产物。

以 macOS 为第一优先平台，减少安装与配置步骤，允许复用系统已安装的外部工具。保留现有 Web 工作台的可用性，当前开发集中在 CLI。

## User Stories

1. As a macOS user, I want to install zimu with clear prerequisites, so that I can start translating without deciphering the repository layout.
2. As a macOS user, I want to launch the same tool with `zimu` or `zm`, so that I can choose the name I remember most easily.
3. As a first-time user, I want `zimu` without arguments to open a guided terminal interface, so that I can discover the main actions without memorizing syntax.
4. As a terminal user, I want the available keyboard actions shown at the bottom of every interactive screen, so that I know how to navigate, confirm, return, and quit.
5. As a terminal user, I want to navigate menus with familiar keys and receive immediate feedback, so that the interface feels responsive.
6. As a script author, I want direct subcommands that do not require interaction, so that zimu works in shell scripts and automation.
7. As a user, I want concise examples in top-level and subcommand help, so that I can learn the shortest way to complete a task.
8. As a user, I want invalid commands and missing arguments to produce actionable errors, so that I can correct them without reading a stack trace.
9. As a script author, I want success and failure represented by appropriate exit statuses, so that my script can distinguish completed work from a failed run.
10. As a script author, I want primary command output separated from progress and error messages, so that outputs can be consumed by another program.
11. As an online-video viewer, I want `translate` to accept a video URL and download the video automatically, so that one command starts the complete translation workflow.
12. As a local-video viewer, I want `translate` to accept a local video file, so that I can use the same workflow without uploading through a browser.
13. As a user, I want `download` to save an online video without translating it, so that I can prepare media separately when needed.
14. As a user, I want English-to-Chinese translation to be the default, so that the common case needs no language flags.
15. As a user, I want the existing `standard` mode to remain the default, so that a normal translation starts with minimal input.
16. As a user, I want to select the existing `meme` mode explicitly, so that I can obtain its more conversational translation and cultural notes when desired.
17. As a user, I want Whisper to transcribe the video's English audio even when source subtitles exist, so that the translation follows the spoken audio consistently.
18. As a standard-mode user, I want the existing automatic LLM proofreading retained, so that its subtitle cleanup behavior remains available without manual editing.
19. As a meme-mode user, I want that mode's current automatic-proofreading setting retained, so that its workflow does not gain an unexpected extra pass.
20. As a user, I want a Chinese SRT as the default finished product, so that I can use or share subtitles without waiting for video encoding.
21. As a user, I want a bilingual SRT when I choose it, so that I can watch or share both source and translated text.
22. As a local-video user, I want the final SRT in the task directory and beside the source video, so that task recovery and everyday file access both work.
23. As a user, I want existing files protected from silent overwrites, so that a rerun does not destroy a subtitle I kept or changed.
24. As a user, I want video subtitle burn-in to be optional, so that the normal subtitle workflow finishes without unnecessary re-encoding.
25. As a user, I want a clear completion screen with the subtitle locations and a play action, so that I can immediately use the result.
26. As a batch user, I want to add multiple URLs and local videos one at a time in the terminal interface, so that I can build a queue without maintaining an input-list file.
27. As a batch user, I want the queue to show a truthful Queue Position, so that I can see what will run next.
28. As a user, I want progress to show the current stage, known completed and total items, and elapsed time, so that I know the tool is working.
29. As a user, I want progress to avoid guessed percentages and completion times, so that displayed information remains trustworthy.
30. As a user, I want a persistent task history with compact identifiers and states, so that I can find unfinished and completed work after reopening zimu.
31. As a user, I want to select an interrupted task in the terminal interface and resume it, so that I do not repeat completed stages unnecessarily.
32. As a script author, I want to resume a task by identifier through a short command, so that recovery can be automated or performed quickly.
33. As a user, I want Stage Checkpoints to be validated before reuse, so that a missing or changed artifact does not produce a false successful result.
34. As a user, I want to be told that resuming may repeat an unfinished model request and incur charges, so that I understand the consequence before continuing.
35. As a user, I want playback to start only when I choose it after translation, so that batch processing does not open many player windows.
36. As a user with a compatible player installed, I want `play` to open the original video with the generated SRT explicitly loaded, so that I see the translation without burning a new video.
37. As a user without such a player, I want zimu to open my default player and show the SRT location and loading guidance, so that playback remains available without a new mandatory dependency.
38. As a user, I want dependency checks to name missing tools and give a next step, so that setup failures are quick to diagnose.
39. As a user, I want model loading, transcription, translation, and automatic proofreading represented as separate stages, so that long-running work is understandable without an invented ETA.
40. As an existing Web-workbench user, I want the current workbench to remain usable, so that a shift in product focus does not abruptly remove my existing workflow.

## Implementation Decisions

- The public product name and installed command names are `zimu` and `zm`. Internal .NET names may migrate in stages; the GitHub repository name is a separate later decision.
- The same application workflow serves direct commands and the guided terminal interface. The CLI entry layer parses requests and renders results; task orchestration, pipeline execution, persistence, and Stage Checkpoint validation remain shared capabilities rather than duplicate implementations.
- No arguments in an interactive terminal open the guided interface. Explicit subcommands remain usable without a terminal; help and errors follow established CLI conventions. Interactive screens reserve a persistent footer for available actions.
- `translate` accepts an online URL or a local video. URL processing includes download. `download` is independently available. The first product language pair is English to Chinese.
- Whisper transcription is the default source of English subtitles even when a source subtitle track or sidecar is available. This is a deliberate consistency choice with a substantial time cost.
- `standard` remains the default and retains automatic LLM proofreading. `meme` remains selectable and keeps its current automatic-proofreading setting. Manual line-by-line review is excluded. The automatic proofreading implementation should avoid waiting when no model request is needed.
- Chinese SRT is the default output, bilingual SRT is optional, and burning subtitles into video is optional rather than part of the default path. A local video's final SRT is retained with the task and copied beside the source video without silently replacing an existing file.
- A durable task identity and history support queueing and resume across process restarts. The CLI should reuse existing persistence and Stage Checkpoint behavior where suitable, while preserving the existing Workbench Task semantics for the Web workflow. Invalid checkpoints are rerun from the first invalid stage.
- Batch entry is guided and incremental in the terminal interface. A list-file import format is not required in this release. Work runs in a bounded queue; Queue Position reflects actual enqueue order.
- Truthful Progress reports current stage, known processed-item count, elapsed time, and Queue Position where applicable. It does not fabricate percentages or ETAs. Long-running external steps should emit timely stage updates without flooding the terminal.
- Playback resolves compatible installed players only when requested. If a player can accept an explicit SRT, zimu opens the original video with that SRT. Otherwise it uses the system default player and presents the subtitle location and loading instructions. Playback does not install or require a particular player.
- External tools remain system-provided. Setup and dependency checks should minimize manual steps; macOS is the first platform to polish. The CLI must never print or persist the value of any API key; configuration messages may name the required environment variable only.
- The existing Web workbench remains usable. No new Web features, GUI, or App development are included in this work.

## Testing Decisions

- Test externally visible command and task behavior rather than private methods, parser internals, or exact terminal escape sequences. The main acceptance seam is the command-to-task boundary: issue a `zimu` or `zm` request against controlled external-tool and model responses, then inspect the user-visible result, durable state, exit status, and produced files.
- Cover direct commands and guided interface navigation at that seam, including the footer guidance, noninteractive help behavior, invalid input, successful output, and failure statuses.
- Cover both URL and local-video input, `standard` and `meme`, default Chinese SRT, optional bilingual SRT and burn-in, adjacent local output, and overwrite protection.
- Cover task ordering, Queue Position, interruption, resume by interface and identifier, Stage Checkpoint reuse, and invalid checkpoint recovery. Model calls should be controlled so a repeated request and its cost warning can be observed deterministically.
- Cover playback with a compatible player present and absent; inspect the invocation and user guidance without opening a real media player in automated checks.
- Cover dependency diagnostics and ensure messages reveal only environment-variable names, never secret values.
- Reuse the style of existing command-argument, Workbench task-store, queue-position, Stage Checkpoint, subtitle-codec, and media-probe tests. Add broad integration tests at the shared application seam only where those narrower examples cannot establish user-visible behavior.
- Measure actual macOS startup, transcription, translation, and proofreading time separately before stating performance targets. A hardware-independent test should verify progress truthfulness and removal of the known fixed proofreading delay, not assert a guessed total runtime.

## Out of Scope

- GUI or App development, new Web-workbench features, or removal of the existing Web workbench.
- Manual subtitle review or line-by-line editing in the terminal.
- Other source/target language pairs in the first release.
- Reusing existing source subtitles instead of Whisper transcription by default.
- Requiring mpv, VLC, or another specific player; building an embedded player.
- List-file batch import, a full-screen subtitle editor, or automatic playback at the end of a batch.
- Renaming the GitHub repository in this spec; cross-platform parity beyond initial macOS polish.

## Further Notes

- Design references: [Mole](https://github.com/tw93/Mole) for a memorable short command and focused utility; [Command Line Interface Guidelines](https://clig.onev.dev/#philosophy) for help, output, errors, discoverability, and composability. The guided terminal interface also follows the user-provided example of a persistent keyboard-hint footer.
- Actual Whisper time depends on video length, machine, model loading, and first-run download. For a 10-minute video, 0.5×, 1×, and 2× real-time processing would take about 20, 10, and 5 minutes for transcription alone; these are arithmetic scenarios, not measured estimates for this project. The current standard proofreading loop has roughly 200 ms of fixed delay per adjacent subtitle pair even without a model request; that implementation cost should be removed while retaining the proofreading feature.
- Publishing this specification authorizes documentation and issue tracking only. Implementation remains deferred by the user's explicit instruction.
