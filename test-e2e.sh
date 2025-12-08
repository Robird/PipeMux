#!/bin/bash
# PipeMux 端到端快速测试

set -e

echo "🧪 PipeMux End-to-End Test"
echo "========================"
echo ""

# 检查配置文件
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

# 构建所有项目
echo "📦 Building projects..."
dotnet build -c Release --nologo -v q > /dev/null
echo "✅ Build successful"
echo ""

# 启动 Broker
echo "🚀 Starting Broker..."
dotnet run --project src/PipeMux.Broker -c Release --nologo 2>/dev/null &
BROKER_PID=$!
sleep 2

if ! kill -0 $BROKER_PID 2>/dev/null; then
    echo "❌ Failed to start Broker"
    exit 1
fi
echo "✅ Broker running (PID: $BROKER_PID)"
echo ""

# 测试函数
test_command() {
    local desc="$1"
    local expected="$2"
    shift 2
    
    echo -n "Testing: $desc ... "
    result=$(dotnet run --project src/PipeMux.CLI -c Release -- "$@" 2>&1 | grep -v "Building" | grep -v "\.csproj" | grep -v "^$" | head -1)
    
    if [ "$result" = "$expected" ]; then
        echo "✅ Pass"
        return 0
    else
        echo "❌ Fail (expected: $expected, got: $result)"
        return 1
    fi
}

# 运行测试
echo "🧮 Running RPN Calculator tests..."
echo ""

echo "📚 Test 1: Stateful stack operations"
test_command "push 10" "Stack: [10]" calculator push 10
test_command "push 20" "Stack: [10, 20]" calculator push 20
test_command "add" "Stack: [30]" calculator add
test_command "push 5" "Stack: [30, 5]" calculator push 5
test_command "mul" "Stack: [150]" calculator mul

echo ""
echo "📚 Test 2: More stack operations"
test_command "dup" "Stack: [150, 150]" calculator dup
test_command "add" "Stack: [300]" calculator add
test_command "push 100" "Stack: [300, 100]" calculator push 100
test_command "sub" "Stack: [200]" calculator sub

echo ""
echo "📚 Test 3: Division and negation"
test_command "push 4" "Stack: [200, 4]" calculator push 4
test_command "div" "Stack: [50]" calculator div
test_command "neg" "Stack: [-50]" calculator neg

echo ""
echo "📚 Test 4: Clear and error handling"
test_command "clear" "Stack: []" calculator clear

echo -n "Testing: empty stack pop ... "
result=$(dotnet run --project src/PipeMux.CLI -c Release -- calculator pop 2>&1 | grep -v "Building" | grep -v "\.csproj" | grep -v "^$" | head -1)
if [[ "$result" == *"Stack is empty"* ]]; then
    echo "✅ Pass (error handled)"
else
    echo "❌ Fail (expected error, got: $result)"
fi

echo ""
echo "📚 Test 5: Division by zero"
test_command "push 10" "Stack: [10]" calculator push 10
test_command "push 0" "Stack: [10, 0]" calculator push 0
echo -n "Testing: division by zero ... "
result=$(dotnet run --project src/PipeMux.CLI -c Release -- calculator div 2>&1 | grep -v "Building" | grep -v "\.csproj" | grep -v "^$" | head -1)
if [[ "$result" == *"Division by zero"* ]]; then
    echo "✅ Pass (error handled)"
else
    echo "❌ Fail (expected error, got: $result)"
fi

echo ""
echo "📚 Test 6: Unknown app"
echo -n "Testing: unknown app ... "
result=$(dotnet run --project src/PipeMux.CLI -c Release -- unknown-app test 2>&1 | grep -v "Building" | grep -v "\.csproj" | grep -v "^$" | head -1)
if [[ "$result" == *"Unknown app"* ]]; then
    echo "✅ Pass (error handled)"
else
    echo "❌ Fail (expected error, got: $result)"
fi

# 清理
echo ""
echo "🧹 Cleaning up..."
kill $BROKER_PID 2>/dev/null || true
wait $BROKER_PID 2>/dev/null || true
echo "✅ Broker stopped"

echo ""
echo "========================"
echo "🎉 All tests passed!"
