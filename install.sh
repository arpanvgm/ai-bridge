
#!/usr/bin/env bash
# AI Bridge — installer for macOS and Linux
# Usage: curl -fsSL https://raw.githubusercontent.com/arpanvgm/ai-bridge/main/install.sh | bash

set -euo pipefail

REPO="arpanvgm/ai-bridge"
BIN_NAME="ai-bridge"
INSTALL_DIR="${HOME}/.ai-bridge/bin"

# ── Detect OS and architecture ─────────────────────────────────
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
      *)        echo "❌ Unsupported Linux architecture: ${ARCH}"; exit 1 ;;
    esac
    ;;
  *)
    echo "❌ Unsupported OS: ${OS}. Use install.ps1 on Windows."
    exit 1
    ;;
esac

ASSET_NAME="ai-bridge-${RID}.zip"

# ── Fetch latest release version ──────────────────────────────
echo "🔍 Fetching latest AI Bridge release..."
LATEST_TAG="$(curl -fsSL "https://api.github.com/repos/${REPO}/releases/latest" \
  | grep '"tag_name"' \
  | sed -E 's/.*"tag_name": *"([^"]+)".*/\1/')"

if [ -z "${LATEST_TAG}" ]; then
  echo "❌ Could not determine latest release. Check your internet connection."
  exit 1
fi

echo "📦 Latest version: ${LATEST_TAG}"

# ── Download and extract ───────────────────────────────────────
DOWNLOAD_URL="https://github.com/${REPO}/releases/download/${LATEST_TAG}/${ASSET_NAME}"
TMP_DIR="$(mktemp -d)"
TMP_ZIP="${TMP_DIR}/${ASSET_NAME}"

echo "⬇️  Downloading ${ASSET_NAME}..."
curl -fsSL --progress-bar "${DOWNLOAD_URL}" -o "${TMP_ZIP}"

echo "📂 Extracting..."
mkdir -p "${INSTALL_DIR}"
unzip -oq "${TMP_ZIP}" -d "${TMP_DIR}"
mv "${TMP_DIR}/${BIN_NAME}" "${INSTALL_DIR}/${BIN_NAME}"
chmod +x "${INSTALL_DIR}/${BIN_NAME}"
rm -rf "${TMP_DIR}"

# ── Remove macOS quarantine attribute ─────────────────────────
if [ "${OS}" = "Darwin" ]; then
  xattr -d com.apple.quarantine "${INSTALL_DIR}/${BIN_NAME}" 2>/dev/null || true
fi

# ── Add to PATH ────────────────────────────────────────────────
add_to_path() {
  local PROFILE_FILE="$1"
  local PATH_LINE="export PATH=\"\${HOME}/.ai-bridge/bin:\${PATH}\""
  if [ -f "${PROFILE_FILE}" ] && grep -q '\.ai-bridge/bin' "${PROFILE_FILE}"; then
    return 0  # already present
  fi
  if [ -f "${PROFILE_FILE}" ]; then
    echo "" >> "${PROFILE_FILE}"
    echo "# AI Bridge" >> "${PROFILE_FILE}"
    echo "${PATH_LINE}" >> "${PROFILE_FILE}"
    echo "✅ Added AI Bridge to PATH in ${PROFILE_FILE}"
  fi
}

case "${SHELL}" in
  */zsh)
    add_to_path "${HOME}/.zshrc"
    ;;
  */bash)
    add_to_path "${HOME}/.bashrc"
    add_to_path "${HOME}/.bash_profile"
    ;;
  */fish)
    fish -c "fish_add_path ${INSTALL_DIR}" 2>/dev/null || \
      echo "⚠️  Add ${INSTALL_DIR} to your fish PATH manually."
    ;;
  *)
    add_to_path "${HOME}/.profile"
    ;;
esac

# ── Done ───────────────────────────────────────────────────────
echo ""
echo "✅ AI Bridge ${LATEST_TAG} installed to ${INSTALL_DIR}/${BIN_NAME}"
echo ""
echo "👉 Reload your shell or run:"
echo "   export PATH=\"\${HOME}/.ai-bridge/bin:\${PATH}\""
echo ""
echo "Then verify with:  ai-bridge --version"
