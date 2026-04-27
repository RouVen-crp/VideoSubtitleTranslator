import argparse
import os
from pathlib import Path
import whisper
import srt
from datetime import timedelta

def transcribe_to_sentence_level_srt(
        audio_path: str,
        output_srt: str,
        model_name: str = "medium",
        language: str = "zh",
        force: bool = False
):
    """
    使用 Whisper 转录音频，直接输出句级（segment）时间戳的 SRT 文件。
    """
    output_path = Path(output_srt)
    audio_p = Path(audio_path)

    # 如果输出文件已存在且比音频新，且不强制，则跳过
    if not force and output_path.exists() and output_path.stat().st_mtime > audio_p.stat().st_mtime:
        print(f"输出文件已存在且较新，直接跳过：{output_srt}")
        return

    print(f"加载模型：{model_name} ...")
    model = whisper.load_model(model_name)

    print(f"开始转录音频：{audio_path}")
    result = model.transcribe(
        audio_path,
        language=language if language else None,
        fp16=False,
        verbose=True,
        word_timestamps=False
    )

    # 生成 SRT subtitles，每个 segment 一条
    subtitles = []
    for i, segment in enumerate(result["segments"]):
        text = segment.get("text", "").strip()
        if not text:
            continue
        start = timedelta(seconds=round(segment["start"], 3))
        end = timedelta(seconds=round(segment["end"], 3))
        subtitles.append(srt.Subtitle(index=i + 1, start=start, end=end, content=text))

    # 保存为 SRT
    with open(output_srt, "w", encoding="utf-8") as f:
        f.write(srt.compose(subtitles))

    print(f"句级 SRT 已生成（{len(subtitles)} 条）：{output_srt}")


def main():
    parser = argparse.ArgumentParser(description="Whisper 音频 → 句级时间戳 SRT")
    parser.add_argument("audio", help="输入音频文件路径")
    parser.add_argument("--output", default=None, help="输出 SRT 路径（默认：原文件名 .word_level.srt）")
    parser.add_argument("--model", default="medium", help="模型大小：tiny/base/small/medium/large/large-v3")
    parser.add_argument("--language", default="zh", help="语言代码：zh/en/ja 等，留空=自动检测")
    parser.add_argument("--force", action="store_true", help="强制重新转录，忽略缓存")

    args = parser.parse_args()

    audio_path = args.audio
    if args.output:
        output_srt = args.output
    else:
        base, _ = os.path.splitext(audio_path)
        output_srt = base + ".word_level.srt"

    transcribe_to_sentence_level_srt(
        audio_path=audio_path,
        output_srt=output_srt,
        model_name=args.model,
        language=args.language if args.language.strip() else None,
        force=args.force
    )

if __name__ == "__main__":
    main()