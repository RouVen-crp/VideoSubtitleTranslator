import { readFileSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const assetsDirectory = join(dirname(fileURLToPath(import.meta.url)), 'assets');

function toMilliseconds(timestamp) {
  const [hours, minutes, seconds, milliseconds] = timestamp.split(/[:,]/).map(Number);
  return ((hours * 60 + minutes) * 60 + seconds) * 1000 + milliseconds;
}

function readSrt(name) {
  const contents = readFileSync(join(assetsDirectory, name), 'utf8').trim();
  return contents.split(/\r?\n\r?\n/).map((block, index) => {
    const lines = block.split(/\r?\n/);
    const times = lines[1]?.match(/^(\d{2}:\d{2}:\d{2},\d{3}) --> (\d{2}:\d{2}:\d{2},\d{3})$/);
    if (Number(lines[0]) !== index + 1 || !times || lines.length < 3) {
      throw new Error(`${name}: invalid subtitle cue ${index + 1}`);
    }
    return {
      start: toMilliseconds(times[1]),
      end: toMilliseconds(times[2]),
      text: lines.slice(2).join('\n')
    };
  });
}

const english = readSrt('sample.en.srt');
const chinese = readSrt('sample.zh.srt');
if (!english.length || english.length !== chinese.length || english.some((cue, index) =>
  cue.start !== chinese[index].start || cue.end !== chinese[index].end)) {
  throw new Error('The sample subtitle files must have matching cue times.');
}

const cues = english.map((cue, index) => ({
  start: cue.start,
  end: cue.end,
  source: cue.text,
  translated: chinese[index].text
}));
const output = `// Generated from sample.en.srt and sample.zh.srt by docs/build-sample-data.mjs.\n` +
  `globalThis.VST_SAMPLE_SUBTITLES = ${JSON.stringify(cues, null, 2)};\n`;
writeFileSync(join(assetsDirectory, 'sample-data.js'), output, 'utf8');
