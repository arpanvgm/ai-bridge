
#!/usr/bin/env bash
# AI Bridge — installer for macOS and Linux
# Usage: curl -fsSL https://raw.githubusercontent.com/arpanvgm/ai-bridge/main/install.sh | bash

set -euo pipefail

REPO="arpanvgm/ai-bridge"
BIN_NAME="ai-bridge"
INSTALL_DIR="${HOME}/.ai-bridge/bin"
TMP_DIR=""

# ── Cleanup trap — always remove temp dir, even on error ──────
cleanup() {
  if [ -n "${TMP_DIR}" ] && [ -d "${TMP_DIR}" ]; then
    rm -rf "${TMP_DIR}"
  fi
}
trap cleanup EXIT

# ── Check required tools ──────────────────────────────────────
for cmd in curl tar; do
  if ! command -v "${cmd}" >/dev/null 2>&1; then
    echo "❌ '${cmd}' is required but was not found on this system."
    echo "   Please install it with your package manager and re-run this script."
    exit 1
  fi
done

# ── Detect OS and architecture ────────────────────────────────
OS="$(uname -s)"
ARCH="$(uname -m)"

case "${OS}" in
  Darwin)
    case "${ARCH}" in
      arm64)  RID="osx-arm64" ;;
      x86_64) RID="osx-x64"   ;;
      *)      echo "❌ Unsupported macOS architecture: ${ARCH}"; exit 1 ;;
    esac
    ;;
  Linux)
    case "${ARCH}" in
      x86_64)  RID="linux-x64"   ;;
      aarch64) RID="linux-arm64" ;;
      *)       echo "❌ Unsupported Linux architecture: ${ARCH}"; exit 1 ;;
    esac
    ;;
  *)
    echo "❌ Unsupported OS: ${OS}. Use install.ps1 on Windows."
    exit 1
    ;;
esac

ASSET_NAME="ai-bridge-${RID}.tar.gz"

# ── Fetch latest release version ─────────────────────────────
echo "🔍 Fetching latest AI Bridge release..."
LATEST_TAG="$(curl -fsSL "https://api.github.com/repos/${REPO}/releases/latest" \
  | grep '"tag_name"' \
  | sed -E 's/.*"tag_name": *"([^"]+)".*/\1/')"

if [ -z "${LATEST_TAG}" ]; then
  echo "❌ Could not determine latest release. Check your internet connection."
  exit 1
fi

echo "📦 Latest version: ${LATEST_TAG}"

# ── Download and extract ──────────────────────────────────────
DOWNLOAD_URL="https://github.com/${REPO}/releases/download/${LATEST_TAG}/${ASSET_NAME}"
TMP_DIR="$(mktemp -d)"
TMP_ARCHIVE="${TMP_DIR}/${ASSET_NAME}"

echo "⬇️  Downloading ${ASSET_NAME}..."
if ! curl -fsSL --progress-bar "${DOWNLOAD_URL}" -o "${TMP_ARCHIVE}"; then
  echo "❌ Download failed: ${DOWNLOAD_URL}"
  echo "   Check your internet connection, or download the file manually from:"
  echo "   https://github.com/${REPO}/releases/latest"
  exit 1
fi

echo "📂 Extracting..."
mkdir -p "${INSTALL_DIR}"
tar -xzf "${TMP_ARCHIVE}" -C "${TMP_DIR}"
mv "${TMP_DIR}/${BIN_NAME}" "${INSTALL_DIR}/${BIN_NAME}"
chmod +x "${INSTALL_DIR}/${BIN_NAME}"

# ── Remove macOS quarantine attribute ─────────────────────────
if [ "${OS}" = "Darwin" ]; then
  xattr -d com.apple.quarantine "${INSTALL_DIR}/${BIN_NAME}" 2>/dev/null || true
fi

# ── Add to PATH ───────────────────────────────────────────────
PATH_LINE="export PATH=\"\${HOME}/.ai-bridge/bin:\${PATH}\""

add_to_path() {
  local PROFILE_FILE="$1"
  if grep -q '\.ai-bridge/bin' "${PROFILE_FILE}" 2>/dev/null; then
    return 0  # already present — nothing to do
  fi
  {
    echo ""
    echo "# AI Bridge"
    echo "${PATH_LINE}"
  } >> "${PROFILE_FILE}"
  echo "✅ Added AI Bridge to PATH in ${PROFILE_FILE}"
}

SHELL_NAME="$(basename "${SHELL:-sh}")"

case "${SHELL_NAME}" in
  zsh)
    ZSHRC="${ZDOTDIR:-${HOME}}/.zshrc"
    touch "${ZSHRC}"
    add_to_path "${ZSHRC}"
    ;;
  bash)
    touch "${HOME}/.bashrc"
    add_to_path "${HOME}/.bashrc"
    # .bash_profile is only used by macOS login shells
    if [ "${OS}" = "Darwin" ] && [ -f "${HOME}/.bash_profile" ]; then
      add_to_path "${HOME}/.bash_profile"
    fi
    ;;
  fish)
    if command -v fish >/dev/null 2>&1; then
      fish -c "fish_add_path ${INSTALL_DIR}"
      echo "✅ Added AI Bridge to PATH in fish."
    else
      echo "⚠️  Could not locate fish. Add ${INSTALL_DIR} to your PATH manually."
    fi
    ;;
  *)
    touch "${HOME}/.profile"
    add_to_path "${HOME}/.profile"
    ;;
esac

# ── Done ──────────────────────────────────────────────────────
echo ""
echo "✅ AI Bridge ${LATEST_TAG} installed to ${INSTALL_DIR}/${BIN_NAME}"
echo ""
echo "👉 Reload your shell or run:"
echo "   export PATH=\"\${HOME}/.ai-bridge/bin:\${PATH}\""
echo ""
echo "Then verify with:  ai-bridge --version"
