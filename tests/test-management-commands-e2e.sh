#!/bin/bash

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"
TEST_HOME="$(mktemp -d /tmp/pipemux-mgmt-e2e-XXXXXX)"
LOG_FILE="$TEST_HOME/broker.log"
CONFIG_PATH="$TEST_HOME/.config/pipemux/broker.toml"
SOCKET_PATH="$TEST_HOME/broker.sock"
BROKER_PID=""

# 直接跑 binary 而非 `dotnet run`，理由：
# 1) 每次 CLI 调用免去 dotnet host 启动开销，脚本快很多；
# 2) BROKER_PID 就是 broker 自己，cleanup 不需要再 pkill -P 兜底；
# 3) 与生产部署形态（systemd 跑 binary）一致。
# 通过 PIPEMUX_SOCKET_PATH 注入隔离 socket 路径，避开 apphost 在隔离 HOME 下
# 对 SpecialFolder.LocalApplicationData 解析为空的怪行为。
export HOME="$TEST_HOME"
export DOTNET_CLI_HOME="$TEST_HOME"
export PIPEMUX_SOCKET_PATH="$SOCKET_PATH"

BROKER_BIN="$ROOT_DIR/src/PipeMux.Broker/bin/Debug/net9.0/PipeMux.Broker"
CLI_BIN="$ROOT_DIR/src/PipeMux.CLI/bin/Debug/net9.0/PipeMux.CLI"

cleanup() {
    if [[ -n "$BROKER_PID" ]] && kill -0 "$BROKER_PID" 2>/dev/null; then
        kill "$BROKER_PID" 2>/dev/null || true
        wait "$BROKER_PID" 2>/dev/null || true
    fi

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

echo "[1/7] Building required projects..."
cd "$ROOT_DIR"
dotnet build PipeMux.sln --nologo > /dev/null
echo "✅ Build successful"
echo ""

echo "[2/7] Starting isolated Broker..."
"$BROKER_BIN" > "$LOG_FILE" 2>&1 &
BROKER_PID=$!

ready=0
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

echo "✅ Broker started (PID: $BROKER_PID)"
echo ""

HOST_DLL="$ROOT_DIR/samples/HostDemo/bin/Debug/net9.0/HostDemo.dll"
HOST_EXE="$ROOT_DIR/src/PipeMux.Host/bin/Debug/net9.0/PipeMux.Host"

echo "[3/7] Registering PipeMux.Host-managed app..."
register_output="$(run_cli :register counter "$HOST_DLL" HostDemo.DebugEntries.BuildCounter --host-path "$HOST_EXE")"
assert_contains "$register_output" "Registered app 'counter'" "register command"

if [[ ! -f "$CONFIG_PATH" ]]; then
    fail "broker.toml was not created after register"
fi

if ! grep -q "HostDemo.DebugEntries.BuildCounter" "$CONFIG_PATH"; then
    fail "broker.toml does not contain the registered app entry"
fi

echo "✅ Register persisted to broker.toml"
echo ""

echo "[4/7] Verifying list and invoke path..."
list_output="$(run_cli :list)"
assert_contains "$list_output" "counter" ":list after register"

invoke_output="$(run_cli counter inc)"
if [[ "$invoke_output" != "Counter: 1" ]]; then
    fail "invoke command: expected 'Counter: 1', got '$invoke_output'"
fi

echo "✅ Registered app listed and invoked successfully"
echo ""

echo "[5/7] Verifying unregister protection..."
if unregister_output="$(run_cli :unregister counter 2>&1)"; then
    fail ":unregister without --stop should have failed"
fi
assert_contains "$unregister_output" "has 1 running process(es)" "unregister protection"

echo "✅ Running process was protected from accidental unregister"
echo ""

echo "[6/7] Unregistering with --stop..."
unregister_stop_output="$(run_cli :unregister counter --stop)"
assert_contains "$unregister_stop_output" "Unregistered app 'counter'" "unregister --stop"
assert_contains "$unregister_stop_output" "stopped 1 process(es)" "unregister --stop count"

if grep -q "HostDemo.DebugEntries.BuildCounter" "$CONFIG_PATH"; then
    fail "broker.toml still contains the app after unregister"
fi

echo "✅ App stopped and removed from broker.toml"
echo ""

echo "[7/7] Verifying post-unregister state..."
final_list_output="$(run_cli :list)"
assert_contains "$final_list_output" "(no applications registered)" "final :list"

if final_invoke_output="$(run_cli counter inc 2>&1)"; then
    fail "invoke after unregister should have failed"
fi
assert_contains "$final_invoke_output" "Unknown app: counter" "invoke after unregister"

echo "✅ Final state is clean"
echo ""
echo "======================================"
echo "✅ Management command tests passed!"
echo "======================================"
