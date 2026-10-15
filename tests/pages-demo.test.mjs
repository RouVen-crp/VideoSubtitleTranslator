import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { test } from 'node:test';
import { runInNewContext } from 'node:vm';

const root = new URL('../', import.meta.url);
const read = (path) => readFileSync(new URL(path, root), 'utf8');

class Element {
  constructor() {
    this.children = [];
    this.attributes = new Map();
    this.listeners = new Map();
    this.dataset = {};
    this.classList = { toggle() {} };
    this.textContent = '';
  }

  addEventListener(type, listener) { this.listeners.set(type, listener); }
  append(...children) { this.children.push(...children); }
  replaceChildren(...children) { this.children = [...children]; }
  setAttribute(name, value) { this.attributes.set(name, value); }
  getAttribute(name) { return this.attributes.get(name); }
  querySelectorAll() { return []; }
  scrollIntoView() {}
  click() { this.listeners.get('click')?.(); }
  remove() {}
}

test('the Pages demo shows all sample cues and switches the video subtitle overlay', async () => {
  const ids = ['video', 'rows', 'subtitleOverlay', 'seek', 'clock', 'playButton',
    'status', 'sampleStatus', 'revision', 'previewMode', 'menuButton', 'navLinks',
    'saveButton', 'downloadButton'];
  const elements = Object.fromEntries(ids.map((id) => [id, new Element()]));
  const previewButtons = [new Element(), new Element()];
  previewButtons[0].dataset.preview = 'zh';
  previewButtons[1].dataset.preview = 'bilingual';
  elements.video.currentTime = 0;
  elements.video.duration = 38.92;
  elements.video.paused = true;
  elements.video.readyState = 4;
  elements.video.play = () => Promise.resolve();
  elements.video.pause = () => {};
  let downloadedBlob;
  const document = {
    body: new Element(),
    createElement: () => new Element(),
    querySelector: (selector) => elements[selector.slice(1)],
    querySelectorAll: (selector) => selector === '[data-preview]' ? previewButtons : []
  };
  const context = {
    document,
    Blob,
    HTMLMediaElement: { HAVE_NOTHING: 0 },
    URL: { createObjectURL: (blob) => { downloadedBlob = blob; return 'blob:sample'; }, revokeObjectURL() {} },
    window: { innerWidth: 1440, setTimeout() {} }
  };

  runInNewContext(read('docs/assets/sample-data.js'), context);
  runInNewContext(read('docs/demo.js'), context);

  assert.equal(elements.rows.children.length, 25);
  assert.match(elements.sampleStatus.textContent, /全部 25 条字幕/);
  assert.equal(elements.subtitleOverlay.children.length, 1);
  assert.equal(elements.subtitleOverlay.children[0].textContent, '埃里克，他们偷了21辆车。');

  previewButtons[1].click();
  assert.equal(elements.previewMode.textContent, '中英双语字幕');
  assert.deepEqual(elements.subtitleOverlay.children.map((item) => item.textContent),
    ['Eric, they stole 21 cars.', '埃里克，他们偷了21辆车。']);

  elements.video.currentTime = 36.06;
  elements.video.listeners.get('timeupdate')();
  assert.equal(elements.subtitleOverlay.children[1].textContent, '不过我还是希望他们能找到那21辆车。');
  previewButtons[0].click();
  assert.equal(elements.previewMode.textContent, '中文字幕');
  assert.equal(elements.subtitleOverlay.children.length, 1);

  elements.downloadButton.click();
  const srt = await downloadedBlob.text();
  assert.match(srt, /25\n00:00:36,060 --> 00:00:38,920\n不过我还是希望他们能找到那21辆车。/);
  assert.equal((srt.match(/ --> /g) ?? []).length, 25);
  assert.match(read('docs/index.html'), /<html lang="zh-CN">/);
});
