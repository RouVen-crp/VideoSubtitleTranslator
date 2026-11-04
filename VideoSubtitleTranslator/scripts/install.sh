#!/bin/sh
# Install the zimu / zm command entry points on macOS and Linux.
set -eu

configuration="Release"
install_dir="${HOME}/.local/bin"

while [ $# -gt 0 ]; do
    case "$1" in
        --configuration)
            [ $# -ge 2 ] || { echo "--configuration requires a value." >&2; exit 2; }
            configuration="$2"
            shift 2
            ;;
        --prefix)
            [ $# -ge 2 ] || { echo "--prefix requires a directory." >&2; exit 2; }
            install_dir="$2"
            shift 2
            ;;
        -h|--help)
            cat <<'USAGE'
Usage: install.sh [--configuration Release|Debug] [--prefix <directory>]

Builds the project and links `zimu` and `zm` into the install directory
(default: ~/.local/bin).
USAGE
            exit 0
            ;;
        *)
            echo "Unknown option: $1" >&2
            exit 2
            ;;
    esac
done

script_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
project_dir=$(CDPATH= cd -- "$script_dir/.." && pwd)
repo_root=$(CDPATH= cd -- "$project_dir/.." && pwd)
solution_path="$repo_root/VideoSubtitleTranslator.sln"

if ! command -v dotnet >/dev/null 2>&1; then
    echo "dotnet was not found. Install the .NET 10 SDK first." >&2
    exit 1
fi

echo "[build] Building $configuration configuration..."
dotnet build "$solution_path" -c "$configuration" -m:1 -nodeReuse:false --nologo -v minimal

output_dir="$project_dir/bin/$configuration/net10.0"
if [ ! -f "$output_dir/zimu" ]; then
    echo "Build output is missing: $output_dir/zimu" >&2
    exit 1
fi

chmod +x "$output_dir/zimu" "$output_dir/zm" 2>/dev/null || true
mkdir -p "$install_dir"
ln -sfn "$output_dir/zimu" "$install_dir/zimu"
ln -sfn "$output_dir/zm" "$install_dir/zm"
echo "[ok] $install_dir/zimu"
echo "[ok] $install_dir/zm"

case ":${PATH}:" in
    *":${install_dir}:"*)
        echo "[ok] $install_dir is already on PATH."
        ;;
    *)
        echo "Add this to your shell profile so the commands stay available:"
        echo "  export PATH=\"$install_dir:\$PATH\""
        ;;
esac

# Prove the entry points start rather than only claiming they were linked.
"$install_dir/zimu" --help >/dev/null 2>&1 || { echo "zimu does not start correctly." >&2; exit 1; }
"$install_dir/zm" --help >/dev/null 2>&1 || { echo "zm does not start correctly." >&2; exit 1; }
echo "[ok] zimu --help and zm --help exited with 0."

echo ""
echo "Installed $install_dir."
echo "Next steps:"
echo "  1. Open a new terminal so PATH changes take effect."
echo "  2. Copy VideoSubtitleTranslator/.env.example to VideoSubtitleTranslator/.env and set DEEPSEEK_API_KEY."
echo "  3. Run: zimu doctor translate"
echo "  4. Run: zimu translate <video URL or local path>"
