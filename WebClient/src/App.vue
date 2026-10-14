<script setup lang="ts">
import { computed, onMounted, ref } from 'vue';

type Status = 'Queued' | 'Processing' | 'AwaitingReview' | 'ExportQueued' | 'Exporting' | 'Completed' | 'Failed' | 'Cancelled' | 'Interrupted';
type Task = { id: string; sourceFileName: string; status: Status; stage: string; processedItems: number; totalItems: number; revision: number; error?: string };
type Item = { id: string; startMs: number; endMs: number; sourceText: string; translatedText: string; needsReview: boolean };
type SubtitleDocument = { taskId: string; revision: number; items: Item[] };
type ExportRow = { export: { id: string; revision: number; bilingual: boolean }; stale: boolean };

const labels: Record<Status, string> = { Queued: '排队中', Processing: '处理中', AwaitingReview: '等待校对', ExportQueued: '等待导出', Exporting: '导出中', Completed: '已完成', Failed: '失败', Cancelled: '已取消', Interrupted: '服务重启中断' };
const tasks = ref<Task[]>([]);
const selected = ref<Task>();
const subtitles = ref<SubtitleDocument>();
const exports = ref<ExportRow[]>([]);
const file = ref<File>();
const message = ref('');
const busy = ref(false);
let refreshTimer: number | undefined;

async function api<T>(url: string, init?: RequestInit): Promise<T> {
  const response = await fetch(url, { headers: { 'Content-Type': 'application/json', ...(init?.headers ?? {}) }, ...init });
  const body = await response.json().catch(() => null);
  if (!response.ok) throw new Error(body?.message ?? '请求失败');
  return body as T;
}

async function refresh() {
  tasks.value = await api<Task[]>('/api/tasks');
  if (selected.value) selected.value = tasks.value.find((task) => task.id === selected.value?.id) ?? selected.value;
  if (tasks.value.some((task) => ['Queued', 'Processing', 'ExportQueued', 'Exporting'].includes(task.status))) {
    window.clearTimeout(refreshTimer);
    refreshTimer = window.setTimeout(refresh, 1500);
  }
}

async function select(task: Task) {
  selected.value = await api<Task>(`/api/tasks/${task.id}`);
  subtitles.value = selected.value.revision ? await api<SubtitleDocument>(`/api/tasks/${task.id}/subtitles`) : undefined;
  exports.value = await api<ExportRow[]>(`/api/tasks/${task.id}/exports`);
}

async function upload() {
  if (!file.value) return;
  busy.value = true;
  try {
    const form = new FormData(); form.append('file', file.value);
    const response = await fetch('/api/tasks', { method: 'POST', body: form, headers: { 'Idempotency-Key': `upload:${file.value.name}:${file.value.size}:${file.value.lastModified}` } });
    const task = await response.json(); if (!response.ok) throw new Error(task.message ?? '上传失败');
    message.value = '任务已创建，后台继续运行。'; await refresh(); await select(task as Task);
  } catch (error) { message.value = (error as Error).message; } finally { busy.value = false; }
}

async function save() {
  if (!selected.value || !subtitles.value) return false;
  try {
    subtitles.value = await api<SubtitleDocument>(`/api/tasks/${selected.value.id}/subtitles`, { method: 'PUT', body: JSON.stringify({ expectedRevision: subtitles.value.revision, items: subtitles.value.items }) });
    message.value = '已保存，源文修改会标记为需要复核。'; await refresh(); return true;
  } catch (error) { message.value = (error as Error).message; return false; }
}

async function retranslate(item: Item) {
  if (!selected.value || !subtitles.value) return;
  try { await api(`/api/tasks/${selected.value.id}/subtitles/retranslate`, { method: 'POST', body: JSON.stringify({ expectedRevision: subtitles.value.revision, subtitleId: item.id, operationId: `retranslate:${selected.value.id}:${item.id}:${subtitles.value.revision}` }) }); message.value = '单句重译已排队。'; } catch (error) { message.value = (error as Error).message; }
}

async function exportVideo(bilingual: boolean) {
  if (!selected.value || !subtitles.value || !(await save())) return;
  try { await api(`/api/tasks/${selected.value.id}/exports`, { method: 'POST', body: JSON.stringify({ expectedRevision: subtitles.value.revision, bilingual, operationId: `export:${selected.value.id}:${subtitles.value.revision}:${bilingual}` }) }); message.value = '导出已排队。'; await refresh(); } catch (error) { message.value = (error as Error).message; }
}

async function cancel() { if (selected.value) { await api(`/api/tasks/${selected.value.id}/cancel`, { method: 'POST' }); await refresh(); await select(selected.value); } }
async function resume() { if (selected.value) { await api(`/api/tasks/${selected.value.id}/resume`, { method: 'POST' }); await refresh(); await select(selected.value); } }
function seek(item: Item) { const video = document.querySelector<HTMLVideoElement>('#video'); if (video) video.currentTime = item.startMs / 1000; }
function format(ms: number) { const total = Math.floor(ms / 1000); return `${String(Math.floor(total / 60)).padStart(2, '0')}:${String(total % 60).padStart(2, '0')}`; }
function percent(task: Task) { return task.totalItems ? Math.min(100, task.processedItems / task.totalItems * 100) : task.status === 'Completed' ? 100 : 18; }
function onFileChange(event: Event) { file.value = (event.target as HTMLInputElement).files?.[0]; }
const canReview = computed(() => !!subtitles.value && !!selected.value);
const selectedStatus = computed<Status>(() => selected.value?.status ?? 'Queued');
onMounted(refresh);
</script>

<template>
  <main class="app-shell">
    <header class="hero"><div><p class="eyebrow">VideoSubtitleTranslator · local workbench</p><h1>逐句校对，再导出成片。</h1><p class="muted">任务、字幕修订和 API Key 都留在本机。</p></div><form class="upload" @submit.prevent="upload"><input type="file" accept="video/mp4" @change="onFileChange"><button :disabled="busy">上传 MP4</button></form></header>
    <p v-if="message" class="message">{{ message }}</p>
    <section class="layout"><aside class="task-list"><div class="section-title"><span>任务</span><button class="secondary" @click="refresh">刷新</button></div><button v-for="task in tasks" :key="task.id" class="task" :class="{ selected: selected?.id === task.id }" @click="select(task)"><strong>{{ task.sourceFileName }}</strong><span>{{ labels[task.status] }} · {{ task.stage }}</span><i><b :style="{ width: `${percent(task)}%` }"></b></i><small v-if="task.error">{{ task.error }}</small></button><p v-if="!tasks.length" class="muted">还没有任务。</p></aside>
      <section class="editor"><div v-if="!canReview" class="empty">选择一个已生成字幕的任务开始校对。</div><template v-else><div class="editor-head"><div><p class="eyebrow">{{ selected?.sourceFileName }}</p><h2>字幕编辑器</h2></div><span>Revision {{ subtitles?.revision }}</span></div><div class="video-row"><video id="video" controls :src="`/api/tasks/${selected?.id}/source`"></video><div class="video-copy"><strong>{{ labels[selectedStatus] }}</strong><p>{{ selected?.stage }}</p><button v-if="['Processing','Queued','ExportQueued','Exporting'].includes(selectedStatus)" class="danger" @click="cancel">取消任务</button><button v-if="['Interrupted','Failed'].includes(selectedStatus)" @click="resume">确认费用后恢复</button></div></div><div v-for="item in subtitles?.items" :key="item.id" class="subtitle-row"><button class="timestamp" @click="seek(item)">{{ format(item.startMs) }}<br>{{ format(item.endMs) }}</button><label><small>英文原文</small><textarea v-model="item.sourceText"></textarea></label><label><small>中文译文</small><textarea v-model="item.translatedText"></textarea><button class="secondary" @click="retranslate(item)">只重译这一条</button></label></div><div class="actions"><button @click="save">保存修订</button><button class="secondary" @click="exportVideo(false)">导出中文字幕</button><button class="secondary" @click="exportVideo(true)">导出中英双语</button></div><div class="exports"><h3>导出记录</h3><p v-for="row in exports" :key="row.export.id"><span :class="{ stale: row.stale }">{{ row.export.bilingual ? '中英双语' : '中文字幕' }} · rev {{ row.export.revision }}{{ row.stale ? ' · 旧版本' : '' }}</span><a :href="`/api/tasks/${selected?.id}/exports/${row.export.id}/video`">MP4</a><a :href="`/api/tasks/${selected?.id}/exports/${row.export.id}/zh-srt`">SRT</a></p></div></template></section></section>
  </main>
</template>
