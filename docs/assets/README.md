# Pages fixed sample assets

- `sample.mp4` — 38-second H.264/AAC sample for broad browser compatibility.
- `sample.en.srt` — normalized English captions.
- `sample.zh.srt` — Chinese captions with exactly matching timestamps.

The English file is the pipeline's normalized subtitle artifact because it is the one that aligns one-to-one with the translated Chinese artifact. Run `node docs/build-sample-data.mjs` from the repository root after changing either SRT file. This regenerates `sample-data.js`, which lets the static demo load all cues even when `docs/index.html` is opened directly as a local file. The demo does not upload visitor media, call an AI service, or create a new video export.

Do not publish the demo until the project owner has confirmed public-display rights for this sample.
