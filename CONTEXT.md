# VideoSubtitleTranslator

This context covers a local video-subtitle translation workbench and its public, static product demonstration. It distinguishes durable translation work from a safe Pages-only demonstration.

## Workbench

**Workbench Task**:
A durable local request to turn one uploaded English MP4 into reviewed subtitles and one or more exported videos.
_Avoid_: Job, upload

**Stage Checkpoint**:
A verified record that one Workbench Task stage and its artifact completed successfully, used to resume without repeating earlier work.
_Avoid_: Cache, progress marker

**Subtitle Revision**:
An immutable, incrementing version of a Workbench Task's editable subtitle document.
_Avoid_: Draft, versioned export

**Export**:
A video and subtitle-file output pinned to exactly one Subtitle Revision.
_Avoid_: Render, latest video

**Truthful Progress**:
The current Workbench Task stage, known processed-item count, elapsed time, and Queue Position when queued, without invented percentages or ETAs.
_Avoid_: Estimated progress, ETA

**Queue Position**:
The one-based order of a queued Workbench Task among work not yet started by the single heavy-work queue.
_Avoid_: Wait estimate, priority

## Public demonstration

**Pages Demo**:
The English, static GitHub Pages experience that demonstrates subtitle playback, seeking, editing, preview, and SRT download without uploading media or invoking AI.
_Avoid_: Web workbench, production app

**Fixed Sample**:
A 20–40 second English video recorded by the project author, with supplied English and Chinese SRT files and public-display rights; it stays unchanged when a Pages Demo visitor edits subtitles.
_Avoid_: Generated export, visitor video

**Subtitle Preview**:
The Pages Demo overlay for a Fixed Sample, shown as Chinese by default and switchable to an aligned English-and-Chinese view.
_Avoid_: Rendered export, generated video

**Interactive SRT**:
The SRT generated in the visitor's browser from their Pages Demo subtitle edits.
_Avoid_: Exported video, AI translation

## Delivery checks

**CI Check**:
An automatic GitHub Actions run that builds the solution and passes the full xUnit suite for a code change.
_Avoid_: Pages deployment, manual regression

**Automated Regression Suite**:
The complete xUnit test suite that checks existing application behavior on each CI Check.
_Avoid_: Smoke test, end-to-end media test
