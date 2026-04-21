#!/bin/bash

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"
TEST_HOME="$(mktemp -d /tmp/pipemux-mgmt-e2e-XXXXXX)"
LOG_FILE="$TEST_HOME/broker.log"
CONFIG_PATH="$TEST_HOME/.config/pipemux/broker.toml"
SOCKET_PATH="$TEST_HOME/broker.sock"
BROKER_PID=""
ORIGINAL_PATH="$PATH"

# 直接跑 binary 而非 `dotnet run`，理由：
# 1) 每次 CLI 调用免去 dotnet host 启动开销，脚本快很多；
# 2) BROKER_PID 就是 broker 自己，cleanup 不需要再 pkill -P 兜底；
# 3) 与生产部署形态（systemd 跑 binary）一致。
# 通过 PIPEMUX_SOCKET_PATH 注入隔离 socket 路径，避开 apphost 在隔离 HOME 下
# 对 SpecialFolder.LocalApplicationData 解析为空的怪行为。
export HOME="$TEST_HOME"
export DOTNET_CLI_HOME="$TEST_HOME"
export PIPEMUX_SOCKET_PATH="$SOCKET_PATH"

BROKER_BIN="$ROOT_DIR/src/PipeMux.Broker/bin/Debug/net10.0/PipeMux.Broker"
CLI_BIN="$ROOT_DIR/src/PipeMux.CLI/bin/Debug/net10.0/PipeMux.CLI"

cleanup() {
    stop_broker >/dev/null 2>&1 || true

    rm -rf "$TEST_HOME"
}

trap cleanup EXIT INT TERM

fail() {
    echo "❌ FAILED: $1"
    if [[ -f "$LOG_FILE" ]]; then
        echo ""
        echo "Broker logs:"
        tail -50 "$LOG_FILE"
    fi
    exit 1
}

run_cli() {
    "$CLI_BIN" "$@"
}

start_broker() {
    "$BROKER_BIN" >> "$LOG_FILE" 2>&1 &
    BROKER_PID=$!

    local ready=0
    for _ in $(seq 1 40); do
        if run_cli :help > /dev/null 2>&1; then
            ready=1
            break
        fi
        sleep 0.25
    done

    if [[ "$ready" -ne 1 ]]; then
        fail "Broker did not become ready"
    fi
}

stop_broker() {
    if [[ -n "$BROKER_PID" ]] && kill -0 "$BROKER_PID" 2>/dev/null; then
        kill "$BROKER_PID" 2>/dev/null || true
        wait "$BROKER_PID" 2>/dev/null || true
    fi

    BROKER_PID=""
}

assert_contains() {
    local actual="$1"
    local expected="$2"
    local description="$3"

    if [[ "$actual" != *"$expected"* ]]; then
        fail "$description: expected output to contain '$expected', got '$actual'"
    fi
}

echo "======================================"
echo "PipeMux Management Commands E2E Test"
echo "======================================"
echo ""

echo "[1/8] Building required projects..."
cd "$ROOT_DIR"
dotnet build PipeMux.sln --nologo > /dev/null
echo "✅ Build successful"
echo ""

HOST_DLL="$ROOT_DIR/samples/HostDemo/bin/Debug/net10.0/HostDemo.dll"
HOST_EXE="$ROOT_DIR/src/PipeMux.Host/bin/Debug/net10.0/PipeMux.Host"
HOST_BIN_DIR="$TEST_HOME/host-bin"
HOST_WRAPPER="$HOST_BIN_DIR/pmux-host"

mkdir -p "$HOST_BIN_DIR"
cat > "$HOST_WRAPPER" <<EOF
#!/usr/bin/env bash
exec "$HOST_EXE" "\$@"
EOF
chmod +x "$HOST_WRAPPER"

# 故意把相对 PATH 段放到 broker 环境里，验证 broker 会把找到的 pmux-host
# 归一化为绝对路径写入 broker.toml，而不是持久化一个依赖当前 cwd/PATH 的相对值。
export PATH="host-bin:$ORIGINAL_PATH"
cd "$TEST_HOME"

echo "[2/8] Starting isolated Broker..."
: > "$LOG_FILE"
start_broker

echo "✅ Broker started (PID: $BROKER_PID)"
echo ""

echo "[2.5/8] Verifying onboarding hints for empty state..."
initial_list_output="$(run_cli :list)"
assert_contains "$initial_list_output" "(no apps registered)" "initial :list empty state"
assert_contains "$initial_list_output" "First-time setup:" "initial :list onboarding header"
assert_contains "$initial_list_output" "[apps.counter]" "initial :list config snippet"
assert_contains "$initial_list_output" "MyNamespace.DebugEntries.BuildCounter" "initial :list entry hint"
assert_contains "$initial_list_output" "pmux :register counter /absolute/path/to/MyApp.dll MyNamespace.DebugEntries.BuildCounter" "initial :list register hint"
assert_contains "$initial_list_output" "command = \"pmux-host /absolute/path/to/MyApp.dll MyNamespace.DebugEntries.BuildCounter\"" "initial :list config hint"

help_output="$(run_cli :help)"
assert_contains "$help_output" "First-time setup:" ":help onboarding header"
assert_contains "$help_output" "[apps.counter]" ":help config snippet"
assert_contains "$help_output" "pmux :register counter /absolute/path/to/MyApp.dll" ":help register example"

echo "✅ Empty-state onboarding is present"
echo ""

echo "[3/8] Registering PipeMux.Host-managed app without --host-path..."
register_output="$(run_cli :register counter "$HOST_DLL" HostDemo.DebugEntries.BuildCounter)"
assert_contains "$register_output" "Registered app 'counter'" "register command"

if [[ ! -f "$CONFIG_PATH" ]]; then
    fail "broker.toml was not created after register"
fi

if ! grep -q "HostDemo.DebugEntries.BuildCounter" "$CONFIG_PATH"; then
    fail "broker.toml does not contain the registered app entry"
fi

if ! grep -Fq "$HOST_WRAPPER" "$CONFIG_PATH"; then
    fail "broker.toml did not persist the resolved absolute pmux-host path"
fi

if grep -Fq 'command = "pmux-host ' "$CONFIG_PATH"; then
    fail "broker.toml should not persist a bare pmux-host command after auto-resolution"
fi

echo "✅ Register persisted to broker.toml"
echo ""

echo "[4/8] Verifying config reload after broker restart without PATH hint..."
export PATH="$ORIGINAL_PATH"
cd "$ROOT_DIR"
stop_broker
start_broker

reloaded_list_output="$(run_cli :list)"
assert_contains "$reloaded_list_output" "counter" ":list after broker restart"

reloaded_invoke_output="$(run_cli counter inc)"
if [[ "$reloaded_invoke_output" != "Counter: 1" ]]; then
    fail "invoke after broker restart: expected 'Counter: 1', got '$reloaded_invoke_output'"
fi

echo "✅ Broker reloaded broker.toml after restart"
echo ""

echo "[5/8] Verifying list and invoke path..."
list_output="$(run_cli :list)"
assert_contains "$list_output" "counter" ":list after register"

invoke_output="$(run_cli counter inc)"
if [[ "$invoke_output" != "Counter: 2" ]]; then
    fail "invoke command: expected 'Counter: 2', got '$invoke_output'"
fi

echo "✅ Registered app listed and invoked successfully"
echo ""

echo "[5.5/8] Verifying re-register guidance when an instance is still running..."
if rereg_output="$(run_cli :register counter "$HOST_DLL" HostDemo.DebugEntries.BuildCounter 2>&1)"; then
    fail "re-register of an existing app should fail"
fi
assert_contains "$rereg_output" "App already registered: counter" "re-register rejection"
assert_contains "$rereg_output" "pmux :stop counter" "re-register stop hint"
assert_contains "$rereg_output" "pmux :unregister counter --stop" "re-register unregister hint"

echo "✅ Re-register surfaces actionable guidance"
echo ""

echo "[6/8] Verifying unregister protection..."
if unregister_output="$(run_cli :unregister counter 2>&1)"; then
    fail ":unregister without --stop should have failed"
fi
assert_contains "$unregister_output" "has 1 running process(es)" "unregister protection"

echo "✅ Running process was protected from accidental unregister"
echo ""

echo "[7/8] Unregistering with --stop..."
unregister_stop_output="$(run_cli :unregister counter --stop)"
assert_contains "$unregister_stop_output" "Unregistered app 'counter'" "unregister --stop"
assert_contains "$unregister_stop_output" "stopped 1 process(es)" "unregister --stop count"

if grep -q "HostDemo.DebugEntries.BuildCounter" "$CONFIG_PATH"; then
    fail "broker.toml still contains the app after unregister"
fi

echo "✅ App stopped and removed from broker.toml"
echo ""

echo "[8/8] Verifying post-unregister state..."
final_list_output="$(run_cli :list)"
assert_contains "$final_list_output" "(no apps registered)" "final :list"
assert_contains "$final_list_output" "First-time setup:" "final :list onboarding header"

if final_invoke_output="$(run_cli counter inc 2>&1)"; then
    fail "invoke after unregister should have failed"
fi
assert_contains "$final_invoke_output" "Unknown app: counter" "invoke after unregister"

echo "✅ Final state is clean"
echo ""
echo "======================================"
echo "✅ Management command tests passed!"
echo "======================================"
