#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_DIR="$(cd "${SCRIPT_DIR}/.." && pwd)"
PYTHON_SCRIPTS_DIR="${PROJECT_DIR}/PythonScripts"
VENV_DIR="${PYTHON_SCRIPTS_DIR}/venv"
REQ_FILE="${PYTHON_SCRIPTS_DIR}/requirements.txt"

PYTHON_CMD="${PYTHON_CMD:-}"
if [[ -z "${PYTHON_CMD}" ]]; then
  for candidate in python3 python; do
    if command -v "${candidate}" >/dev/null 2>&1 && "${candidate}" -c 'import sys' >/dev/null 2>&1; then
      PYTHON_CMD="$(command -v "${candidate}")"
      break
    fi
  done
fi

if [[ -z "${PYTHON_CMD}" ]]; then
  echo "python3/python 未安装，请先安装 Python 3。"
  exit 1
fi

"${PYTHON_CMD}" -m venv "${VENV_DIR}"

# Git Bash on Windows creates Scripts/python.exe, while Unix uses bin/python3.
if [[ -f "${VENV_DIR}/Scripts/python.exe" ]]; then
  VENV_PYTHON="${VENV_DIR}/Scripts/python.exe"
elif [[ -f "${VENV_DIR}/bin/python3" ]]; then
  VENV_PYTHON="${VENV_DIR}/bin/python3"
elif [[ -f "${VENV_DIR}/bin/python" ]]; then
  VENV_PYTHON="${VENV_DIR}/bin/python"
else
  echo "无法找到虚拟环境 Python：${VENV_DIR}"
  exit 1
fi

"${VENV_PYTHON}" -m pip install --upgrade pip
"${VENV_PYTHON}" -m pip install -r "${REQ_FILE}"

echo "Python 虚拟环境初始化完成：${VENV_DIR}"
