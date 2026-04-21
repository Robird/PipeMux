#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd -- "${SCRIPT_DIR}/.." && pwd)"

CONFIGURATION="${CONFIGURATION:-Release}"
RUNTIME="${RUNTIME:-linux-x64}"
SELF_CONTAINED="${SELF_CONTAINED:-false}"
INSTALL_ROOT="${INSTALL_ROOT:-$HOME/.local/share/pipemux}"
BIN_DIR="${BIN_DIR:-$HOME/.local/bin}"
CONFIG_DIR="${CONFIG_DIR:-$HOME/.config/pipemux}"
SYSTEMD_USER_DIR="${SYSTEMD_USER_DIR:-$HOME/.config/systemd/user}"
SERVICE_NAME="pipemux-broker.service"

SKIP_SYSTEMD=0

while (($# > 0)); do
    case "$1" in
        --skip-systemd)
            SKIP_SYSTEMD=1
            shift
            ;;
        *)
            echo "Unknown argument: $1" >&2
            echo "Usage: $0 [--skip-systemd]" >&2
            exit 1
            ;;
    esac
done

stage_dir="$(mktemp -d)"
trap 'rm -rf "$stage_dir"' EXIT

publish_project() {
    local project_path="$1"
    local output_dir="$2"

    dotnet publish "$project_path" \
        -c "$CONFIGURATION" \
        -r "$RUNTIME" \
        --self-contained "$SELF_CONTAINED" \
        -o "$output_dir"
}

sync_dir() {
    local source_dir="$1"
    local target_dir="$2"

    mkdir -p "$target_dir"
    if command -v rsync >/dev/null 2>&1; then
        rsync -a --delete "$source_dir"/ "$target_dir"/
    else
        cp -a "$source_dir"/. "$target_dir"/
    fi
}

write_wrapper() {
    local target_path="$1"
    local exec_path="$2"

    cat > "$target_path" <<EOF
#!/usr/bin/env bash
exec "$exec_path" "\$@"
EOF
    chmod +x "$target_path"
}

write_cli_wrapper() {
    local target_path="$1"
    local exec_path="$2"

    cat > "$target_path" <<EOF
#!/usr/bin/env bash
set -euo pipefail

SERVICE_NAME="pipemux-broker.service"

if [[ "\${PIPEMUX_NO_AUTOSTART:-0}" != "1" ]] && command -v systemctl >/dev/null 2>&1; then
    if systemctl --user --quiet is-enabled "\$SERVICE_NAME" 2>/dev/null || systemctl --user --quiet is-active "\$SERVICE_NAME" 2>/dev/null; then
        if ! systemctl --user --quiet is-active "\$SERVICE_NAME" 2>/dev/null; then
            systemctl --user start "\$SERVICE_NAME" >/dev/null 2>&1 || true
        fi
    fi
fi

exec "$exec_path" "\$@"
EOF
    chmod +x "$target_path"
}

write_default_config_if_missing() {
    local config_path="$CONFIG_DIR/broker.toml"
    if [[ -f "$config_path" ]]; then
        echo "Keeping existing config: $config_path"
        return
    fi

    cat > "$config_path" <<EOF
# PipeMux Broker 配置

[broker]
socket_path = "~/.local/share/pipemux/broker.sock"

# 如需强制使用 named pipe，可改为：
# pipe_name = "pipemux-broker"

# 示例：使用 PipeMux.Host 托管任意 DLL 中的入口
# [apps.counter]
# command = "pmux-host /absolute/path/to/MyApp.dll MyNamespace.DebugEntries.BuildCounter"
# 如需显式路径，也可以写成：
# command = "$INSTALL_ROOT/bin/host/PipeMux.Host /absolute/path/to/MyApp.dll MyNamespace.DebugEntries.BuildCounter"
# auto_start = false
# timeout = 30
EOF
    echo "Created default config: $config_path"
}

install_service_file() {
    local source_service="$REPO_ROOT/deploy/systemd/user/$SERVICE_NAME"
    local target_service="$SYSTEMD_USER_DIR/$SERVICE_NAME"
    cp "$source_service" "$target_service"
    echo "Installed user service: $target_service"
}

mkdir -p "$INSTALL_ROOT/bin/broker" "$INSTALL_ROOT/bin/cli" "$INSTALL_ROOT/bin/host"
mkdir -p "$BIN_DIR" "$CONFIG_DIR" "$SYSTEMD_USER_DIR"

echo "Publishing PipeMux.Broker..."
publish_project "$REPO_ROOT/src/PipeMux.Broker/PipeMux.Broker.csproj" "$stage_dir/broker"

echo "Publishing PipeMux.CLI..."
publish_project "$REPO_ROOT/src/PipeMux.CLI/PipeMux.CLI.csproj" "$stage_dir/cli"

echo "Publishing PipeMux.Host..."
publish_project "$REPO_ROOT/src/PipeMux.Host/PipeMux.Host.csproj" "$stage_dir/host"

echo "Installing binaries into $INSTALL_ROOT ..."
sync_dir "$stage_dir/broker" "$INSTALL_ROOT/bin/broker"
sync_dir "$stage_dir/cli" "$INSTALL_ROOT/bin/cli"
sync_dir "$stage_dir/host" "$INSTALL_ROOT/bin/host"

write_cli_wrapper "$BIN_DIR/pmux" "$INSTALL_ROOT/bin/cli/PipeMux.CLI"
write_wrapper "$BIN_DIR/pmux-host" "$INSTALL_ROOT/bin/host/PipeMux.Host"

write_default_config_if_missing
install_service_file

if [[ "$SKIP_SYSTEMD" -eq 0 ]]; then
    echo "Reloading user systemd daemon..."
    systemctl --user daemon-reload
    echo "Enabling and restarting $SERVICE_NAME ..."
    systemctl --user enable --now "$SERVICE_NAME"
    systemctl --user restart "$SERVICE_NAME"
    echo "Service status:"
    systemctl --user --no-pager --full status "$SERVICE_NAME" || true
else
    echo "Skipping systemd actions (--skip-systemd)."
fi

cat <<EOF

Install/update completed.

Installed files:
  Broker: $INSTALL_ROOT/bin/broker/PipeMux.Broker
  CLI:    $INSTALL_ROOT/bin/cli/PipeMux.CLI
  Host:   $INSTALL_ROOT/bin/host/PipeMux.Host
  Config: $CONFIG_DIR/broker.toml
  Service:$SYSTEMD_USER_DIR/$SERVICE_NAME

Command shortcuts:
  $BIN_DIR/pmux
  $BIN_DIR/pmux-host

If '$BIN_DIR' is not on PATH, add this line to your shell rc:
  export PATH="$BIN_DIR:\$PATH"
EOF
