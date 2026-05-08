# 任务
执行当前短视频模块的中译，风格要求口语化、有网感、保留meme氛围。
同步维护：术语一致性表、元翻译指令、前情提要、广告概括。

## 输出契约（严格）
只输出一个 JSON 对象（不要 markdown 代码块、不要注释、不要解释）：
{"translations":["..."],"termEdits":[{"action":"add|update|delete","key":"...","value":"..."}],"metaRuleEdits":[{"action":"add|update|delete","key":"...","value":"..."}],"synopsisFullText":"...","adSummaryUpdate":"..."}

## 翻译风格要求
- 口语化优先：不要书面语，用中国人日常聊天的语气。
- 俚语等效：英文俚语/黑话/街头用语可用中文网络流行语等效替换。
- 保留语气：讽刺、挑衅、自嘲、夸张等语气必须保留。
- 短句为主：短视频字幕空间有限，每句尽量简短。

## 字段要求
- `translations`：长度必须与当前模块英文句数完全一致，顺序一一对应，不可漏译，不可并句拆句。
- `termEdits`：术语增量；无修改返回 `[]`。不得写入广告相关专名。
- `metaRuleEdits`：元规则增量；无修改返回 `[]`。规则应可复用，避免临时碎片信息。
- `synopsisFullText`：更新后的完整前情提要，必须是单段连贯中文，聚焦正片主线并排除广告；若无需更新返回 `""`。
- `adSummaryUpdate`：若本模块出现广告口播，输出 1-2 句中文概括；无广告返回 `""`。

## 输入上下文
{{DOMAIN_HINT_SECTION}}

### 程序内置广告策略
{{INTERNAL_AD_POLICY_PROMPT}}

### 已记录广告概括（来自 ad_memory.txt）
{{AD_SUMMARIES}}

{{REPAIR_SECTION}}### 视频标题译文
{{TRANSLATED_TITLE}}

### 当前术语表（正片专名）
{{TERM_TABLE}}

### 当前元翻译指令
{{META_RULES}}

### 当前前情提要（仅主线）
{{SYNOPSIS_PARAGRAPH}}

### 上文模块（含译文）
{{PREVIOUS_MODULES}}

### 下文模块（原文）
{{NEXT_MODULES}}

### 当前模块原文
{{CURRENT_MODULE}}

## 最终校验
- `translations` 数组长度必须严格等于 `{{EXPECTED_COUNT}}`。
- 最终输出必须是单个 JSON 对象，字段名使用英文键名。
