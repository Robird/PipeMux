#!/bin/bash

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"
TEST_HOME="$(mktemp -d /tmp/pipemux-mgmt-e2e-XXXXXX)"
LOG_FILE="$TEST_HOME/broker.log"
CONFIG_PATH="$TEST_HOME/.config/pipemux/broker.toml"
BROKER_ENV_PATH="$TEST_HOME/.config/pipemux/broker.env"
BROKER_DROPIN_PATH="$TEST_HOME/.config/systemd/user/pipemux-broker.service.d/10-environment.conf"
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

run_cli_in_terminal() {
    local terminal_id="$1"
    shift
    (
        export PIPEMUX_TERMINAL_ID="$terminal_id"
        "$CLI_BIN" "$@"
    )
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

echo "[1/11] Building required projects..."
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

echo "[2/11] Starting isolated Broker..."
: > "$LOG_FILE"
start_broker

echo "✅ Broker started (PID: $BROKER_PID)"
echo ""

echo "[2.5/11] Verifying onboarding hints for empty state..."
initial_list_output="$(run_cli :list)"
assert_contains "$initial_list_output" "(no apps registered)" "initial :list empty state"
assert_contains "$initial_list_output" "First-time setup:" "initial :list onboarding header"
assert_contains "$initial_list_output" "[apps.counter]" "initial :list config snippet"
assert_contains "$initial_list_output" "MyNamespace.DebugEntries.BuildCounter" "initial :list entry hint"
assert_contains "$initial_list_output" "pmux :register counter /absolute/path/to/MyApp.dll MyNamespace.DebugEntries.BuildCounter" "initial :list register hint"
assert_contains "$initial_list_output" "command = \"pmux-host /absolute/path/to/MyApp.dll MyNamespace.DebugEntries.BuildCounter\"" "initial :list config hint"
assert_contains "$initial_list_output" "assembly_path = \"/absolute/path/to/MyApp.dll\"" "initial :list assembly path hint"

help_output="$(run_cli :help)"
assert_contains "$help_output" "First-time setup:" ":help onboarding header"
assert_contains "$help_output" "[apps.counter]" ":help config snippet"
assert_contains "$help_output" "pmux :register counter /absolute/path/to/MyApp.dll" ":help register example"
assert_contains "$help_output" ":reload" ":help reload command"
assert_contains "$help_output" ":copy-env-to-broker" ":help copy-env-to-broker command"

echo "✅ Empty-state onboarding is present"
echo ""

echo "[2.7/11] Verifying broker environment copy command..."
export DEEPSEEK_API_KEY="e2e-deepseek-key"
copy_env_output="$(run_cli :copy-env-to-broker DEEPSEEK_API_KEY MISSING_KEY)"
assert_contains "$copy_env_output" "Copied 1 environment variable(s) to broker environment: DEEPSEEK_API_KEY" ":copy-env-to-broker copied variables"
assert_contains "$copy_env_output" "Environment file: $BROKER_ENV_PATH" ":copy-env-to-broker env file path"
assert_contains "$copy_env_output" "Missing in current CLI environment: MISSING_KEY" ":copy-env-to-broker missing variable hint"
assert_contains "$copy_env_output" "Systemd drop-in created: $BROKER_DROPIN_PATH" ":copy-env-to-broker drop-in path"
assert_contains "$copy_env_output" "systemctl --user daemon-reload && systemctl --user restart pipemux-broker" ":copy-env-to-broker restart hint"

if [[ ! -f "$BROKER_ENV_PATH" ]]; then
    fail "broker.env was not created by :copy-env-to-broker"
fi

if [[ ! -f "$BROKER_DROPIN_PATH" ]]; then
    fail "systemd drop-in was not created by :copy-env-to-broker"
fi

if ! grep -Fq 'DEEPSEEK_API_KEY="e2e-deepseek-key"' "$BROKER_ENV_PATH"; then
    fail "broker.env did not persist the copied environment variable"
fi

broker_env_mode="$(stat -c %a "$BROKER_ENV_PATH")"
if [[ "$broker_env_mode" != "600" ]]; then
    fail "broker.env permissions were $broker_env_mode instead of 600"
fi

if ! grep -Fq 'EnvironmentFile=-%h/.config/pipemux/broker.env' "$BROKER_DROPIN_PATH"; then
    fail "systemd drop-in did not point to broker.env"
fi

echo "✅ Broker environment copy command persisted broker.env and drop-in"
echo ""

echo "[3/11] Registering PipeMux.Host-managed app without --host-path..."
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

if ! grep -Fq "assembly_path = \"$HOST_DLL\"" "$CONFIG_PATH"; then
    fail "broker.toml did not persist assembly_path"
fi

if grep -Fq 'command = "pmux-host ' "$CONFIG_PATH"; then
    fail "broker.toml should not persist a bare pmux-host command after auto-resolution"
fi

echo "✅ Register persisted to broker.toml"
echo ""

echo "[4/11] Verifying manual broker.toml edits can be reloaded without restarting broker..."
RELOADED_ASSEMBLY_PATH="$TEST_HOME/manual-edit.dll"
sed -i "s|^assembly_path = \".*\"$|assembly_path = \"$RELOADED_ASSEMBLY_PATH\"|" "$CONFIG_PATH"

reload_output="$(run_cli :reload)"
assert_contains "$reload_output" "Reloaded broker config:" ":reload success"

list_after_reload_output="$(run_cli :list)"
assert_contains "$list_after_reload_output" "Assembly: $RELOADED_ASSEMBLY_PATH (file not found)" ":list should reflect reloaded assembly_path"

sed -i "s|^assembly_path = \".*\"$|assembly_path = \"$HOST_DLL\"|" "$CONFIG_PATH"
reload_restore_output="$(run_cli :reload)"
assert_contains "$reload_restore_output" "Reloaded broker config:" ":reload restore success"

restored_list_output="$(run_cli :list)"
assert_contains "$restored_list_output" "Assembly: $HOST_DLL" ":list should reflect restored assembly_path"

echo "✅ Reload picked up hand-edited broker.toml"
echo ""

echo "[5/11] Verifying config reload after broker restart without PATH hint..."
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

echo "[6/11] Verifying list and invoke path..."
list_output="$(run_cli :list)"
assert_contains "$list_output" "counter" ":list after register"

invoke_output="$(run_cli counter inc)"
if [[ "$invoke_output" != "Counter: 2" ]]; then
    fail "invoke command: expected 'Counter: 2', got '$invoke_output'"
fi

echo "✅ Registered app listed and invoked successfully"
echo ""

echo "[6.5/11] Verifying re-register guidance when an instance is still running..."
if rereg_output="$(run_cli :register counter "$HOST_DLL" HostDemo.DebugEntries.BuildCounter 2>&1)"; then
    fail "re-register of an existing app should fail"
fi
assert_contains "$rereg_output" "App already registered: counter" "re-register rejection"
assert_contains "$rereg_output" "pmux :stop counter" "re-register stop hint"
assert_contains "$rereg_output" "pmux :unregister counter --stop" "re-register unregister hint"

echo "✅ Re-register surfaces actionable guidance"
echo ""

echo "[7/11] Verifying multi-terminal restart preserves isolated instances..."
stop_output="$(run_cli :stop counter)"
assert_contains "$stop_output" "Stopped:" ":stop before multi-terminal restart test"

terminal_a_output="$(run_cli_in_terminal term-a counter inc)"
if [[ "$terminal_a_output" != "Counter: 1" ]]; then
    fail "terminal A initial invoke: expected 'Counter: 1', got '$terminal_a_output'"
fi

terminal_b_output="$(run_cli_in_terminal term-b counter inc)"
if [[ "$terminal_b_output" != "Counter: 1" ]]; then
    fail "terminal B initial invoke: expected 'Counter: 1', got '$terminal_b_output'"
fi

ps_before_restart="$(run_cli :ps)"
assert_contains "$ps_before_restart" "counter:env:term-a" ":ps before restart should include terminal A"
assert_contains "$ps_before_restart" "counter:env:term-b" ":ps before restart should include terminal B"

restart_output="$(run_cli :restart counter)"
assert_contains "$restart_output" "Restarted 2 processes for: counter" ":restart should preserve instance count"

ps_after_restart="$(run_cli :ps)"
assert_contains "$ps_after_restart" "counter:env:term-a" ":ps after restart should include terminal A"
assert_contains "$ps_after_restart" "counter:env:term-b" ":ps after restart should include terminal B"

terminal_a_after_restart="$(run_cli_in_terminal term-a counter inc)"
if [[ "$terminal_a_after_restart" != "Counter: 1" ]]; then
    fail "terminal A after restart: expected 'Counter: 1', got '$terminal_a_after_restart'"
fi

terminal_b_after_restart="$(run_cli_in_terminal term-b counter inc)"
if [[ "$terminal_b_after_restart" != "Counter: 1" ]]; then
    fail "terminal B after restart: expected 'Counter: 1', got '$terminal_b_after_restart'"
fi

echo "✅ Restart keeps per-terminal instances isolated"
echo ""

echo "[8/11] Verifying :restart does not implicitly start a stopped app..."
stop_after_restart_test="$(run_cli :stop counter)"
assert_contains "$stop_after_restart_test" "Stopped 2 processes for: counter" ":stop after restart test"

if restart_stopped_output="$(run_cli :restart counter 2>&1)"; then
    fail ":restart should fail when the app is registered but not running"
fi
assert_contains "$restart_stopped_output" "No running process found for: counter" ":restart stopped app failure"

ps_after_failed_restart="$(run_cli :ps)"
assert_contains "$ps_after_failed_restart" "(no running processes)" ":ps should stay empty after failed restart"

echo "✅ Restart stays scoped to running instances"
echo ""

terminal_a_restarted="$(run_cli_in_terminal term-a counter inc)"
if [[ "$terminal_a_restarted" != "Counter: 1" ]]; then
    fail "terminal A after failed restart should cold-start: expected 'Counter: 1', got '$terminal_a_restarted'"
fi

terminal_b_restarted="$(run_cli_in_terminal term-b counter inc)"
if [[ "$terminal_b_restarted" != "Counter: 1" ]]; then
    fail "terminal B after failed restart should cold-start: expected 'Counter: 1', got '$terminal_b_restarted'"
fi

echo "[9/11] Verifying unregister protection..."
if unregister_output="$(run_cli :unregister counter 2>&1)"; then
    fail ":unregister without --stop should have failed"
fi
assert_contains "$unregister_output" "has 2 running process(es)" "unregister protection"

echo "✅ Running process was protected from accidental unregister"
echo ""

echo "[10/11] Unregistering with --stop..."
unregister_stop_output="$(run_cli :unregister counter --stop)"
assert_contains "$unregister_stop_output" "Unregistered app 'counter'" "unregister --stop"
assert_contains "$unregister_stop_output" "stopped 2 process(es)" "unregister --stop count"

if grep -q "HostDemo.DebugEntries.BuildCounter" "$CONFIG_PATH"; then
    fail "broker.toml still contains the app after unregister"
fi

echo "✅ App stopped and removed from broker.toml"
echo ""

echo "[11/11] Verifying post-unregister state..."
final_list_output="$(run_cli :list)"
assert_contains "$final_list_output" "(no apps registered)" "final :list"
assert_contains "$final_list_output" "First-time setup:" "final :list onboarding header"

if final_invoke_output="$(run_cli counter inc 2>&1)"; then
    fail "invoke after unregister should have failed"
fi
assert_contains "$final_invoke_output" "Unknown app: counter" "invoke after unregister"
assert_contains "$final_invoke_output" "Run \`pmux :list\` to see registered apps." "unknown app hint"

echo "✅ Final state is clean"
echo ""
echo "======================================"
echo "✅ Management command tests passed!"
echo "======================================"
