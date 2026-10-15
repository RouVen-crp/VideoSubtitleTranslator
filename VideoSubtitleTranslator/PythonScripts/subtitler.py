import argparse
import os
import subprocess
from pathlib import Path
import srt
from datetime import timedelta

try:
    import whisper as openai_whisper
except Exception:
    # On some Windows installations the Torch DLL cannot initialize even though
    # the rest of the environment is healthy. Keep the documented Whisper
    # backend as the first choice and let the FFmpeg fallback handle this case.
    openai_whisper = None


WHISPER_MODEL_DIR = Path(__file__).resolve().parent / "models" / "whisper"


def _filter_path(path: str) -> str:
    """Quote a path for an FFmpeg filter option, including Windows drive letters."""
    value = path.replace("\\", "/").replace("'", "\\'").replace(":", "\\:")
    return f"'{value}'"


def _transcribe_with_ffmpeg(
        audio_path: str,
        output_srt: str,
        language: str,
        ffmpeg_path: str,
        model_path: str,
        use_gpu: bool,
) -> None:
    if not model_path:
        raise RuntimeError("未配置 whisper.cpp 模型文件，请设置 VST_WHISPER_MODEL_PATH 或 whisper.modelPath。")
    if not Path(model_path).is_file():
        raise RuntimeError(f"whisper.cpp 模型文件不存在：{model_path}")

    temporary_srt = f"{output_srt}.ffmpeg.tmp"
    filter_description = (
        f"whisper=model={_filter_path(model_path)}:language={language or 'auto'}:"
        f"use_gpu={1 if use_gpu else 0}:format=srt:destination={_filter_path(temporary_srt)}"
    )
    try:
        result = subprocess.run(
            [
                ffmpeg_path,
                "-y",
                "-hide_banner",
                "-loglevel",
                "error",
                "-i",
                audio_path,
                "-af",
                filter_description,
                "-f",
                "null",
                "NUL" if os.name == "nt" else "/dev/null",
            ],
            capture_output=True,
            text=True,
            check=False,
        )
    except FileNotFoundError as exc:
        raise RuntimeError(f"找不到 FFmpeg：{ffmpeg_path}") from exc

    if result.returncode != 0:
        details = (result.stderr or result.stdout or "").strip()
        if len(details) > 2000:
            details = details[-2000:]
        raise RuntimeError(f"FFmpeg whisper.cpp 转录失败（exit code {result.returncode}）：{details}")

    if not Path(temporary_srt).is_file():
        raise RuntimeError("FFmpeg whisper.cpp 未生成 SRT 文件。")
    parsed = list(srt.parse(Path(temporary_srt).read_text(encoding="utf-8-sig")))
    if not parsed:
        raise RuntimeError("FFmpeg whisper.cpp 未生成有效字幕条目。")
    Path(output_srt).parent.mkdir(parents=True, exist_ok=True)
    Path(output_srt).write_text(srt.compose(parsed, reindex=True), encoding="utf-8")
    Path(temporary_srt).unlink(missing_ok=True)


def transcribe_to_sentence_level_srt(
        audio_path: str,
        output_srt: str,
        model_name: str = "medium",
        language: str = "zh",
        force: bool = False,
        ffmpeg_path: str = "ffmpeg",
        whisper_model_path: str = "",
        whisper_use_gpu: bool = False,
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
    subtitles = []
    if openai_whisper is not None:
        try:
            model = openai_whisper.load_model(model_name, download_root=str(WHISPER_MODEL_DIR))
            print(f"开始转录音频：{audio_path}")
            result = model.transcribe(
                audio_path,
                language=language if language else None,
                fp16=False,
                verbose=True,
                word_timestamps=False
            )
            segments = result["segments"]
            for segment in segments:
                text = segment.get("text", "").strip()
                if not text:
                    continue
                start = timedelta(seconds=round(segment["start"], 3))
                end = timedelta(seconds=round(segment["end"], 3))
                subtitles.append(srt.Subtitle(index=len(subtitles) + 1, start=start, end=end, content=text))
        except Exception as exc:
            print(f"openai-whisper 不可用，将尝试 FFmpeg whisper.cpp：{exc}", file=os.sys.stderr)
            subtitles.clear()

    if not subtitles and whisper_model_path:
        print(f"openai-whisper 不可用，使用 FFmpeg whisper.cpp：{audio_path}")
        _transcribe_with_ffmpeg(audio_path, output_srt, language, ffmpeg_path, whisper_model_path, whisper_use_gpu)
        return

    if not subtitles:
        raise RuntimeError("无法加载 openai-whisper；请重新运行 scripts/init_python_venv.sh，或配置 whisper.modelPath 使用 FFmpeg whisper.cpp fallback。")

    # 保存为 SRT
    with open(output_srt, "w", encoding="utf-8") as f:
        f.write(srt.compose(subtitles))

    print(f"句级 SRT 已生成（{len(subtitles)} 条）：{output_srt}")


def main():
    parser = argparse.ArgumentParser(description="Whisper 音频 → 句级时间戳 SRT")
    parser.add_argument("audio", help="输入音频文件路径")
    parser.add_argument("--output", default=None, help="输出 SRT 路径（默认：原文件名 .word_level.srt）")
    parser.add_argument("--model", default="medium", help="模型大小：tiny/base/small/medium/large/large-v3/turbo")
    parser.add_argument("--language", default="zh", help="语言代码：zh/en/ja 等，留空=自动检测")
    parser.add_argument("--force", action="store_true", help="强制重新转录，忽略缓存")
    parser.add_argument("--ffmpeg", default=os.environ.get("VST_FFMPEG_PATH", "ffmpeg"), help="FFmpeg 可执行文件路径")
    parser.add_argument("--whisper-model-path", default=os.environ.get("VST_WHISPER_MODEL_PATH", ""), help="whisper.cpp 模型文件路径")
    parser.add_argument("--whisper-use-gpu", action="store_true", help="使用 FFmpeg whisper.cpp 的 GPU 推理")

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
        force=args.force,
        ffmpeg_path=args.ffmpeg,
        whisper_model_path=args.whisper_model_path,
        whisper_use_gpu=args.whisper_use_gpu,
    )

if __name__ == "__main__":
    main()
