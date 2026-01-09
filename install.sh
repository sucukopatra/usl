#!/usr/bin/env bash
# USL installer (Unix)

set -e

# Where to install
BIN_DIR="$HOME/.local/bin"
mkdir -p "$BIN_DIR"

# Project root = where this script lives
PROJECT_ROOT="$(cd "$(dirname "$0")" && pwd)"

# Wrapper path
WRAPPER="$BIN_DIR/usl"

cat > "$WRAPPER" <<EOF
#!/usr/bin/env bash
PYTHONPATH="$PROJECT_ROOT" python3 "$PROJECT_ROOT/run.py" "\$@"
EOF

chmod +x "$WRAPPER"

echo "USL installed successfully!"
echo "Make sure $BIN_DIR is in your PATH."
echo "You can now run: usl list"
