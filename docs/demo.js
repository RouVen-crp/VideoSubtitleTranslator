const video = document.querySelector('#video');
const rows = document.querySelector('#rows');
const overlay = document.querySelector('#subtitleOverlay');
const seek = document.querySelector('#seek');
const clock = document.querySelector('#clock');
const playButton = document.querySelector('#playButton');
const status = document.querySelector('#status');
const sampleStatus = document.querySelector('#sampleStatus');
const revision = document.querySelector('#revision');
const previewMode = document.querySelector('#previewMode');
const followButton = document.querySelector('#followButton');
const menuButton = document.querySelector('#menuButton');
const navLinks = document.querySelector('#navLinks');
const subtitles = Array.isArray(globalThis.VST_SAMPLE_SUBTITLES)
  ? globalThis.VST_SAMPLE_SUBTITLES.map((cue) => ({ ...cue }))
  : [];
let preview = 'zh';
let revisionNumber = 1;
let followEnabled = true;
let editingFocused = false;
let lastFollowedIndex = -1;
const assetsReady = subtitles.length > 0;

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
  return subtitles.find((item) => now >= item.start && now < item.end);
}

function currentSubtitleIndex() {
  const now = video.currentTime * 1000;
  return subtitles.findIndex((item) => now >= item.start && now < item.end);
}

function followTargetIndex() {
  const now = video.currentTime * 1000;
  const activeIndex = subtitles.findIndex((item) => now >= item.start && now < item.end);
  if (activeIndex >= 0) return activeIndex;
  for (let index = subtitles.length - 1; index >= 0; index--) {
    if (subtitles[index].end <= now) return index;
  }
  return -1;
}

function scrollRowIntoView(index) {
  const row = rows.children[index];
  if (!row) return;
  const rowTop = row.getBoundingClientRect().top - rows.getBoundingClientRect().top + rows.scrollTop;
  const rowBottom = rowTop + row.offsetHeight;
  const behavior = window.matchMedia('(prefers-reduced-motion: reduce)').matches ? 'auto' : 'smooth';
  if (rowTop < rows.scrollTop) {
    rows.scrollTo({ top: rowTop, behavior });
  } else if (rowBottom > rows.scrollTop + rows.clientHeight) {
    rows.scrollTo({ top: rowBottom - rows.clientHeight, behavior });
  }
}

function updateFollowButton() {
  const editingPause = followEnabled && editingFocused;
  followButton.setAttribute('aria-pressed', String(followEnabled));
  followButton.textContent = editingPause ? '编辑时暂停' : followEnabled ? '已开启跟随' : '跟随播放';
  followButton.setAttribute('aria-label', editingPause
    ? '正在编辑，自动跟随已暂停；离开编辑框后恢复'
    : followEnabled ? '自动跟随播放中的字幕，点击暂停跟随' : '已暂停自动跟随，点击跟随播放中的字幕');
}

function setFollowEnabled(enabled) {
  followEnabled = enabled;
  updateFollowButton();
}

function updateActiveRow() {
  const activeIndex = currentSubtitleIndex();
  [...rows.children].forEach((row, index) => {
    const isActive = index === activeIndex;
    row.classList.toggle('is-active', isActive);
    if (isActive) row.setAttribute('aria-current', 'true');
    else row.removeAttribute('aria-current');
  });
  followCurrentCue();
}

function followCurrentCue(force = false, allowPaused = false) {
  if ((video.paused && !allowPaused) || !followEnabled || (editingFocused && !force)) return;
  const targetIndex = followTargetIndex();
  if (targetIndex < 0 || (!force && targetIndex === lastFollowedIndex)) return;
  lastFollowedIndex = targetIndex;
  scrollRowIntoView(targetIndex);
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
    const seekButton = document.createElement('button'); seekButton.type = 'button'; seekButton.className = 'timestamp'; seekButton.textContent = `${formatTime(item.start)}–${formatTime(item.end)}`; seekButton.setAttribute('aria-label', `跳转到第 ${index + 1} 条字幕，${formatTime(item.start)}`);
    seekButton.addEventListener('click', () => {
      video.currentTime = item.start / 1000;
      if (window.innerWidth <= 1023) video.scrollIntoView({ block: 'center' });
      updateTransport();
      playFixedSample();
    });
    const copy = document.createElement('div'); copy.className = 'subtitle-copy';
    const source = document.createElement('p'); source.className = 'source'; source.textContent = item.source;
    const label = document.createElement('label'); label.textContent = `第 ${index + 1} 条 · 中文译文`;
    const textarea = document.createElement('textarea'); textarea.value = item.translated; textarea.rows = 2;
    textarea.addEventListener('input', () => { subtitles[index].translated = textarea.value; updateOverlay(); });
    textarea.addEventListener('focus', () => { editingFocused = true; updateFollowButton(); });
    textarea.addEventListener('blur', () => {
      requestAnimationFrame(() => {
        editingFocused = rows.contains(document.activeElement) && document.activeElement instanceof HTMLTextAreaElement;
        updateFollowButton();
        if (!editingFocused) followCurrentCue(true);
      });
    });
    label.append(textarea); copy.append(source, label); row.append(seekButton, copy); rows.append(row);
  });
  updateActiveRow();
}

function updateTransport() {
  const duration = Number.isFinite(video.duration) ? video.duration : 0;
  seek.max = String(Math.max(duration, 1));
  seek.value = String(Math.min(video.currentTime, duration || 0));
  clock.value = `${formatTime(video.currentTime * 1000)} / ${formatTime(duration * 1000)}`;
  playButton.textContent = video.paused ? '播放' : '暂停';
  playButton.setAttribute('aria-label', video.paused ? '播放样例视频' : '暂停样例视频');
  updateOverlay();
  updateActiveRow();
}

function makeSrt() {
  return subtitles.map((item, index) => `${index + 1}\n${srtTime(item.start)} --> ${srtTime(item.end)}\n${item.translated}`).join('\n\n') + '\n';
}

function loadSample() {
  if (!assetsReady) {
    sampleStatus.textContent = '完整样例字幕加载失败，请刷新页面后重试。';
    document.querySelector('#saveButton').disabled = true;
    document.querySelector('#downloadButton').disabled = true;
    return;
  }
  sampleStatus.textContent = `已加载全部 ${subtitles.length} 条字幕。点击时间戳可跳转到对应画面。`;
  renderRows();
  updateOverlay();
}

function playFixedSample() {
  if (video.readyState === HTMLMediaElement.HAVE_NOTHING) {
    sampleStatus.textContent = '样例视频尚未就绪，请稍后再试。';
    return;
  }
  video.play().catch(() => { sampleStatus.textContent = '当前浏览器无法播放样例视频。'; });
}

playButton.addEventListener('click', () => { if (video.paused) playFixedSample(); else video.pause(); });
seek.addEventListener('input', () => { video.currentTime = Number(seek.value); updateTransport(); });
video.addEventListener('timeupdate', updateTransport);
video.addEventListener('loadedmetadata', updateTransport);
video.addEventListener('play', updateTransport);
video.addEventListener('pause', updateTransport);
video.addEventListener('error', () => { if (assetsReady) sampleStatus.textContent = '当前浏览器无法播放样例视频。'; });
followButton.addEventListener('click', () => {
  const enabled = followButton.getAttribute('aria-pressed') !== 'true';
  setFollowEnabled(enabled);
  if (enabled) followCurrentCue(true, true);
});

function suspendFollowForManualScroll() {
  if (followEnabled) setFollowEnabled(false);
}

rows.addEventListener('wheel', suspendFollowForManualScroll, { passive: true });
rows.addEventListener('touchmove', suspendFollowForManualScroll, { passive: true });
rows.addEventListener('pointerdown', (event) => {
  const bounds = rows.getBoundingClientRect();
  if (event.target === rows && event.clientX >= bounds.right - 16) suspendFollowForManualScroll();
});
rows.addEventListener('keydown', (event) => {
  if (event.target instanceof HTMLTextAreaElement || event.target instanceof HTMLButtonElement) return;
  if (['ArrowDown', 'ArrowUp', 'PageDown', 'PageUp', 'Home', 'End', ' '].includes(event.key)) suspendFollowForManualScroll();
});

document.querySelectorAll('[data-preview]').forEach((button) => button.addEventListener('click', () => {
  preview = button.dataset.preview;
  previewMode.textContent = preview === 'bilingual' ? '中英双语字幕' : '中文字幕';
  document.querySelectorAll('[data-preview]').forEach((item) => {
    const active = item.dataset.preview === preview;
    item.classList.toggle('active', active); item.setAttribute('aria-pressed', String(active));
  });
  if (subtitles.length && video.currentTime * 1000 >= subtitles[subtitles.length - 1].end) {
    video.currentTime = subtitles[0].start / 1000;
  }
  updateOverlay();
}));

document.querySelector('#saveButton').addEventListener('click', () => {
  revisionNumber++;
  revision.textContent = `修订 ${revisionNumber}`;
  status.textContent = '已保存本次浏览器内的修订；样例视频文件未改变。';
});

document.querySelector('#downloadButton').addEventListener('click', () => {
  const url = URL.createObjectURL(new Blob([makeSrt()], { type: 'application/x-subrip;charset=utf-8' }));
  const link = document.createElement('a'); link.href = url; link.download = 'sample.edited.zh.srt';
  document.body.append(link);
  link.click();
  link.remove();
  window.setTimeout(() => URL.revokeObjectURL(url), 1000);
  status.textContent = `已下载包含全部 ${subtitles.length} 条字幕的中文 SRT。`;
});

loadSample();

menuButton.addEventListener('click', () => {
  const isOpen = menuButton.getAttribute('aria-expanded') === 'true';
  menuButton.setAttribute('aria-expanded', String(!isOpen));
  menuButton.setAttribute('aria-label', isOpen ? '打开导航菜单' : '关闭导航菜单');
  navLinks.classList.toggle('open', !isOpen);
});

navLinks.querySelectorAll('a').forEach((link) => link.addEventListener('click', () => {
  menuButton.setAttribute('aria-expanded', 'false');
  menuButton.setAttribute('aria-label', '打开导航菜单');
  navLinks.classList.remove('open');
}));
