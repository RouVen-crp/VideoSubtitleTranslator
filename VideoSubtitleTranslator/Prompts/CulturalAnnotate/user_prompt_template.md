# 任务
对以下短视频句子的译文判断是否需要文化注释。仅对需要注释的句子提供一行背景说明（不超过40字），不解释笑话本身。

## 输出契约（严格）
只输出一个 JSON 对象：
{
  "annotations": [
    {"index": 1, "needsAnnotation": false, "annotation": ""},
    {"index": 2, "needsAnnotation": true, "annotation": "一句话背景说明"},
    ...
  ],
  "termEdits": [],
  "contextUpdate": ""
}

## 字段要求
- `annotations`：数组长度必须与输入句子数一致，index 与输入编号对应。
- `needsAnnotation`：true/false。
- `annotation`：仅当 needsAnnotation 为 true 时填写，不超过 40 个中文字。
- `termEdits`：可复用的文化专名增量维护（{"action":"add|update|delete","key":"名词","value":"解释"}）。无修改返回 `[]`。
- `contextUpdate`：若本次注释揭示了可复用的视频全局文化背景，写一段中文概括更新。无需更新返回 `""`。

## 输入上下文

### 当前文化术语表
{{CULTURAL_TERM_TABLE}}

### 当前文化背景上下文
{{CULTURAL_CONTEXT}}

### 原文字幕
{{ORIGINAL_SENTENCES}}

### 中文字幕
{{TRANSLATED_SENTENCES}}
