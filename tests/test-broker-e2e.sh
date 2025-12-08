#!/bin/bash
# End-to-End test for PipeMux Broker process management and JSON-RPC communication

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"
LOG_FILE="/tmp/broker-test.log"

echo "======================================"
echo "PipeMux Broker E2E Test Suite"
echo "======================================"
echo ""

# Check config file
CONFIG_PATH="$HOME/.config/pipemux/broker.toml"
if [ ! -f "$CONFIG_PATH" ]; then
    echo "📝 Creating default config file..."
    mkdir -p "$(dirname "$CONFIG_PATH")"
    cat > "$CONFIG_PATH" << 'TOMLEOF'
[broker]
pipe_name = "pipemux-broker"

[apps.calculator]
command = "dotnet run --project /repos/PieceTreeSharp/PipeMux/samples/Calculator"
auto_start = false
timeout = 30
TOMLEOF
    echo "✅ Config created: $CONFIG_PATH"
    echo ""
fi

# Cleanup function
cleanup() {
    echo ""
    echo "Cleaning up..."
    pkill -9 -f "PipeMux.Broker" 2>/dev/null || true
    pkill -9 -f "Samples.Calculator" 2>/dev/null || true
    sleep 1
}

# Set trap to cleanup on exit
trap cleanup EXIT

# Clean up any existing processes
cleanup

# Start Broker
echo "[1/8] Starting Broker..."
cd "$ROOT_DIR"
nohup dotnet run --project src/PipeMux.Broker > "$LOG_FILE" 2>&1 &
BROKER_PID=$!
sleep 2

# Check if Broker started successfully
if ! ps -p $BROKER_PID > /dev/null; then
    echo "❌ FAILED: Broker failed to start"
    cat "$LOG_FILE"
    exit 1
fi
echo "✅ Broker started (PID: $BROKER_PID)"
echo ""

# Test 1: First request (cold start)
echo "[2/8] Test: First request (cold start)..."
RESULT=$(dotnet run --project src/PipeMux.CLI -- calculator add 5 3 2>/dev/null)
if [ "$RESULT" != "8" ]; then
    echo "❌ FAILED: Expected 8, got $RESULT"
    exit 1
fi
echo "✅ Result: $RESULT"
sleep 0.5

# Verify process was started
if ! grep -q "Starting new process for calculator" "$LOG_FILE"; then
    echo "❌ FAILED: Process should have been started"
    exit 1
fi
echo "✅ Process started successfully"
echo ""

# Test 2: Second request (process reuse)
echo "[3/8] Test: Second request (process reuse)..."
RESULT=$(dotnet run --project src/PipeMux.CLI -- calculator multiply 7 6 2>/dev/null)
if [ "$RESULT" != "42" ]; then
    echo "❌ FAILED: Expected 42, got $RESULT"
    exit 1
fi
echo "✅ Result: $RESULT"
sleep 0.5

# Verify process was reused
if ! grep -q "Reusing existing process" "$LOG_FILE"; then
    echo "❌ FAILED: Process should have been reused"
    exit 1
fi
echo "✅ Process reused successfully"
echo ""

# Test 3: Concurrent requests
echo "[4/8] Test: Concurrent requests..."
dotnet run --project src/PipeMux.CLI -- calculator add 1 2 2>/dev/null &
PID1=$!
dotnet run --project src/PipeMux.CLI -- calculator subtract 10 3 2>/dev/null &
PID2=$!
dotnet run --project src/PipeMux.CLI -- calculator multiply 4 5 2>/dev/null &
PID3=$!
wait $PID1 $PID2 $PID3
echo "✅ All concurrent requests completed"
echo ""

# Test 4: Error handling (division by zero)
echo "[5/8] Test: Error handling (division by zero)..."
RESULT=$(dotnet run --project src/PipeMux.CLI -- calculator divide 10 0 2>&1 | grep -v "^Building\|^Restore\|MSBuild\|^  ")
if [[ "$RESULT" != *"Division by zero"* ]]; then
    echo "❌ FAILED: Expected error message, got '$RESULT'"
    exit 1
fi
echo "✅ Error handled correctly: $RESULT"
echo ""

# Test 5: Unknown app
echo "[6/8] Test: Unknown app error..."
RESULT=$(dotnet run --project src/PipeMux.CLI -- unknown-app test 123 2>&1 | grep -v "^Building\|^Restore\|MSBuild\|^  ")
if [[ "$RESULT" != *"Unknown app"* ]]; then
    echo "❌ FAILED: Expected 'Unknown app' error, got '$RESULT'"
    exit 1
fi
echo "✅ Error handled correctly: $RESULT"
echo ""

# Test 6: Process crash recovery
echo "[7/8] Test: Process crash recovery..."
CALC_PID=$(ps aux | grep "PipeMux.Calculator/bin" | grep -v grep | awk '{print $2}' | head -1)
if [ -n "$CALC_PID" ]; then
    echo "Killing calculator process (PID: $CALC_PID)..."
    kill -9 $CALC_PID
    sleep 1
fi

RESULT=$(dotnet run --project src/PipeMux.CLI -- calculator add 100 200 2>/dev/null)
if [ "$RESULT" != "300" ]; then
    echo "❌ FAILED: Expected 300 after recovery, got $RESULT"
    exit 1
fi
echo "✅ Process restarted after crash, result: $RESULT"
echo ""

# Test 7: All operations
echo "[8/8] Test: All calculator operations..."
TESTS=(
    "add 15 25:40"
    "subtract 100 42:58"
    "multiply 12 8:96"
    "divide 144 12:12"
)

for test in "${TESTS[@]}"; do
    CMD="${test%%:*}"
    EXPECTED="${test##*:}"
    RESULT=$(dotnet run --project src/PipeMux.CLI -- calculator $CMD 2>/dev/null)
    if [ "$RESULT" != "$EXPECTED" ]; then
        echo "❌ FAILED: $CMD expected $EXPECTED, got $RESULT"
        exit 1
    fi
    echo "  ✅ $CMD = $RESULT"
done
echo ""

# Summary
echo "======================================"
echo "✅ All tests passed!"
echo "======================================"
echo ""
echo "Broker logs:"
tail -20 "$LOG_FILE"
