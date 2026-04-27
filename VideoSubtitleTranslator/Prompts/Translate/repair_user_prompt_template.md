# 任务
将下列英文逐条翻译为中文（用于缺句修复）。

## 输出契约（严格）
只输出 JSON：
{"translations":["..."]}

## 约束
- 不要输出任何额外文字。
- `translations` 数组长度必须等于 `{{COUNT}}`。
- `translations` 顺序必须与输入完全一致。

## 输入
{{LINES}}
