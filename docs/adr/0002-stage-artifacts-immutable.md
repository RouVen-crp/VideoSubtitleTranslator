# ADR-0002: 阶段产物归阶段所有

- 状态：已接受
- 日期：2026-11-05

## 背景

`ProofreadSubtitle` 一直把校对结果同时写回自己的产物 `translated_subtitle.proofread.srt` 和 `Translate` 的产物 `translated_subtitle.srt`。两个入口的恢复逻辑（CLI 的 `CliPipelineExecutor`、Web 工作台的 `StageRecovery`）都把 `Translate` 检查点绑定在 `translated_subtitle.srt` 的哈希上。于是只要校对成功过，`Translate` 检查点必然失效：恢复时会从 `Translate` 开始作废，重跑两段模型调用（重新产生费用）并丢弃已有的校对产物。

同一覆写还让双语字幕无法稳定对齐：#16 的双语 SRT 与 #28 的双语 ASS 都按序号把中文块和英文原文行配对，而校对会合并相邻句，合并之后序号发生位移，英文就会错位。

## 决策

每个流水线阶段的产物只由该阶段写入。`ProofreadSubtitle` 只写校对产物，不再覆写 `Translate` 的 `translated_subtitle.srt`。下游与恢复逻辑只读取离自己最近的有效产物（校对优先、翻译原文兜底），检查点哈希只反映该阶段自己的输出。

副本产物（双语 SRT、烧录 ASS）改为按**时间重叠**把中文块与英文原文行配对，不再依赖条数与序号相等。

## 考虑过的替代方案

- **保留覆写，把 `Translate` 检查点改登记到 `raw_translated_subtitle.txt`**：改动更小，但旧任务的检查点仍指向被覆写的文件；并且 `Translate` 的跳过守卫可能让新登记文件根本不生成，CLI 又只在产物存在时登记检查点，会留下"重跑了却没有新检查点"的空转。
- **继续按序号配对、遇到条数不一致时报错**：等于要求校对永远不合并句子，与校对功能本身冲突。

## 后果

- 已存在的 Failed/Interrupted 旧任务检查点仍然失效（它们是在旧行为下记录的），恢复会重跑一次 `Translate`+`ProofreadSubtitle`；不做一次性迁移。
- 双语 SRT 与双语 ASS 对"校对合并相邻句"是容忍的：每个英文原文行归属重叠最多的中文块，合并块会把多条英文拼在一起。
- 未来新增阶段时，任何跨阶段覆写产物都会重新引入本次修复的缺陷。
