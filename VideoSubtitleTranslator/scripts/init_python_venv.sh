#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_DIR="$(cd "${SCRIPT_DIR}/.." && pwd)"
PYTHON_SCRIPTS_DIR="${PROJECT_DIR}/PythonScripts"
VENV_DIR="${PYTHON_SCRIPTS_DIR}/venv"
REQ_FILE="${PYTHON_SCRIPTS_DIR}/requirements.txt"

if ! command -v python3 >/dev/null 2>&1; then
  echo "python3 未安装，请先安装 Python 3。"
  exit 1
fi

python3 -m venv "${VENV_DIR}"
source "${VENV_DIR}/bin/activate"
python -m pip install --upgrade pip
python -m pip install -r "${REQ_FILE}"

echo "Python 虚拟环境初始化完成：${VENV_DIR}"
