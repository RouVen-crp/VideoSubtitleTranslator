const fallbackSubtitles = [
  { start: 0, end: 4200, source: 'Review every line before you export.', translated: '导出前，逐句检查字幕。' },
  { start: 4200, end: 8200, source: 'The demo never calls an AI service.', translated: '这个演示不会调用任何 AI 服务。' }
];

const video = document.querySelector('#video');
const rows = document.querySelector('#rows');
const overlay = document.querySelector('#subtitleOverlay');
const seek = document.querySelector('#seek');
const clock = document.querySelector('#clock');
const playButton = document.querySelector('#playButton');
const status = document.querySelector('#status');
const sampleStatus = document.querySelector('#sampleStatus');
const revision = document.querySelector('#revision');
let subtitles = [];
let preview = 'zh';
let revisionNumber = 1;
let assetsReady = false;

function parseTimestamp(value) {
  const [hours, minutes, seconds] = value.trim().replace(',', '.').split(':');
  return (Number(hours) * 3600 + Number(minutes) * 60 + Number(seconds)) * 1000;
}

function parseSrt(source) {
  return source.trim().split(/\r?\n\r?\n/).map((block) => {
    const lines = block.split(/\r?\n/).filter(Boolean);
    const times = lines[1]?.split(/\s+-->\s+/);
    if (!times || lines.length < 3) throw new Error('The supplied SRT is malformed.');
    return { start: parseTimestamp(times[0]), end: parseTimestamp(times[1]), text: lines.slice(2).join('\n') };
  });
}

function formatTime(milliseconds) {
  const total = Math.max(0, Math.floor(milliseconds / 1000));
  const hours = Math.floor(total / 3600);
  const minutes = Math.floor((total % 3600) / 60);
  const seconds = total % 60;
  return `${hours ? `${String(hours).padStart(2, '0')}:` : ''}${String(minutes).padStart(2, '0')}:${String(seconds).padStart(2, '0')}`;
}

function srtTime(milliseconds) {
  const value = Math.max(0, Math.floor(milliseconds));
  const hours = Math.floor(value / 3600000);
  const minutes = Math.floor((value % 3600000) / 60000);
  const seconds = Math.floor((value % 60000) / 1000);
  const ms = value % 1000;
  return `${String(hours).padStart(2, '0')}:${String(minutes).padStart(2, '0')}:${String(seconds).padStart(2, '0')},${String(ms).padStart(3, '0')}`;
}

function currentSubtitle() {
  const now = video.currentTime * 1000;
  return subtitles.find((item) => now >= item.start && now <= item.end);
}

function updateOverlay() {
  const item = currentSubtitle();
  if (!item) { overlay.replaceChildren(); return; }
  overlay.replaceChildren();
  if (preview === 'bilingual') {
    const source = document.createElement('span'); source.className = 'subtitle-source'; source.textContent = item.source;
    overlay.append(source);
  }
  const translated = document.createElement('span'); translated.className = 'subtitle-translated'; translated.textContent = item.translated;
  overlay.append(translated);
}

function renderRows() {
  rows.replaceChildren();
  subtitles.forEach((item, index) => {
    const row = document.createElement('article'); row.className = 'subtitle-row';
    const seekButton = document.createElement('button'); seekButton.type = 'button'; seekButton.className = 'timestamp'; seekButton.textContent = `${formatTime(item.start)}–${formatTime(item.end)}`;
    seekButton.addEventListener('click', () => { video.currentTime = item.start / 1000; playFixedSample(); });
    const copy = document.createElement('div'); copy.className = 'subtitle-copy';
    const source = document.createElement('p'); source.className = 'source'; source.textContent = item.source;
    const label = document.createElement('label'); label.textContent = 'Chinese translation';
    const textarea = document.createElement('textarea'); textarea.value = item.translated; textarea.rows = 2;
    textarea.addEventListener('input', () => { subtitles[index].translated = textarea.value; updateOverlay(); });
    label.append(textarea); copy.append(source, label); row.append(seekButton, copy); rows.append(row);
  });
}

function updateTransport() {
  const duration = Number.isFinite(video.duration) ? video.duration : 0;
  seek.max = String(Math.max(duration, 1));
  seek.value = String(Math.min(video.currentTime, duration || 0));
  clock.value = `${formatTime(video.currentTime * 1000)} / ${formatTime(duration * 1000)}`;
  playButton.textContent = video.paused ? 'Play' : 'Pause';
  updateOverlay();
}

function makeSrt() {
  return subtitles.map((item, index) => `${index + 1}\n${srtTime(item.start)} --> ${srtTime(item.end)}\n${item.translated}`).join('\n\n') + '\n';
}

async function loadSample() {
  try {
    const [englishResponse, chineseResponse] = await Promise.all(['assets/sample.en.srt', 'assets/sample.zh.srt'].map((asset) => fetch(asset)));
    if (!englishResponse.ok || !chineseResponse.ok) throw new Error('Missing sample assets');
    const english = parseSrt(await englishResponse.text());
    const chinese = parseSrt(await chineseResponse.text());
    if (!english.length || english.length !== chinese.length || english.some((line, index) => line.start !== chinese[index].start || line.end !== chinese[index].end)) throw new Error('Unaligned sample subtitles');
    subtitles = english.map((line, index) => ({ ...line, source: line.text, translated: chinese[index].text }));
    assetsReady = true;
    sampleStatus.textContent = 'Fixed project sample. Playback and seeking are active.';
  } catch {
    subtitles = fallbackSubtitles.map((item) => ({ ...item }));
    sampleStatus.textContent = 'Sample video and SRT files are not included yet. The editor is shown with placeholder copy only.';
  }
  renderRows(); updateOverlay();
}

function playFixedSample() {
  if (video.readyState === HTMLMediaElement.HAVE_NOTHING) {
    sampleStatus.textContent = 'Add the fixed sample video before using playback.';
    return;
  }
  video.play().catch(() => { sampleStatus.textContent = 'The supplied sample video could not be played in this browser.'; });
}

playButton.addEventListener('click', () => { if (video.paused) playFixedSample(); else video.pause(); });
seek.addEventListener('input', () => { video.currentTime = Number(seek.value); updateTransport(); });
video.addEventListener('timeupdate', updateTransport);
video.addEventListener('loadedmetadata', updateTransport);
video.addEventListener('play', updateTransport);
video.addEventListener('pause', updateTransport);
video.addEventListener('error', () => { if (assetsReady) sampleStatus.textContent = 'The supplied sample video could not be played in this browser.'; });

document.querySelectorAll('[data-preview]').forEach((button) => button.addEventListener('click', () => {
  preview = button.dataset.preview;
  document.querySelectorAll('[data-preview]').forEach((item) => {
    const active = item.dataset.preview === preview;
    item.classList.toggle('active', active); item.setAttribute('aria-pressed', String(active));
  });
  updateOverlay();
}));

document.querySelector('#saveButton').addEventListener('click', () => {
  revisionNumber++;
  revision.textContent = `Revision ${revisionNumber}`;
  status.textContent = 'Saved in this browser only. The fixed sample video is unchanged.';
});

document.querySelector('#downloadButton').addEventListener('click', () => {
  const url = URL.createObjectURL(new Blob([makeSrt()], { type: 'application/x-subrip;charset=utf-8' }));
  const link = document.createElement('a'); link.href = url; link.download = 'sample.edited.zh.srt'; link.click();
  URL.revokeObjectURL(url); status.textContent = 'Your edited Chinese SRT has been downloaded.';
});

loadSample();
