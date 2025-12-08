#!/bin/bash
# Manual test script for PipeMux.Calculator
# This script demonstrates how to manually test the calculator service

set -e

echo "=== PipeMux.Calculator Manual Test Suite ==="
echo ""

# Build the project
echo "Building PipeMux.Calculator..."
dotnet build samples/Calculator -q
echo "✓ Build successful"
echo ""

# Test 1: Addition
echo "Test 1: Addition (5 + 3)"
echo '{"jsonrpc": "2.0", "id": 1, "method": "add", "params": {"a": 5, "b": 3}}' | \
    dotnet run --project samples/Calculator 2>/dev/null
echo ""

# Test 2: Subtraction
echo "Test 2: Subtraction (10 - 4)"
echo '{"jsonrpc": "2.0", "id": 2, "method": "subtract", "params": {"a": 10, "b": 4}}' | \
    dotnet run --project samples/Calculator 2>/dev/null
echo ""

# Test 3: Multiplication
echo "Test 3: Multiplication (6 × 7)"
echo '{"jsonrpc": "2.0", "id": 3, "method": "multiply", "params": {"a": 6, "b": 7}}' | \
    dotnet run --project samples/Calculator 2>/dev/null
echo ""

# Test 4: Division
echo "Test 4: Division (20 ÷ 4)"
echo '{"jsonrpc": "2.0", "id": 4, "method": "divide", "params": {"a": 20, "b": 4}}' | \
    dotnet run --project samples/Calculator 2>/dev/null
echo ""

# Test 5: Division by zero (error case)
echo "Test 5: Division by zero (should return error)"
echo '{"jsonrpc": "2.0", "id": 5, "method": "divide", "params": {"a": 10, "b": 0}}' | \
    dotnet run --project samples/Calculator 2>/dev/null
echo ""

# Test 6: Unknown method (error case)
echo "Test 6: Unknown method (should return method not found)"
echo '{"jsonrpc": "2.0", "id": 6, "method": "unknown", "params": {"a": 1, "b": 2}}' | \
    dotnet run --project samples/Calculator 2>/dev/null
echo ""

# Test 7: Missing parameters (error case)
echo "Test 7: Missing parameters (should return invalid params)"
echo '{"jsonrpc": "2.0", "id": 7, "method": "add", "params": null}' | \
    dotnet run --project samples/Calculator 2>/dev/null
echo ""

# Test 8: Invalid JSON (error case)
echo "Test 8: Invalid JSON (should return parse error)"
echo 'invalid json' | dotnet run --project samples/Calculator 2>/dev/null
echo ""

# Test 9: Batch request
echo "Test 9: Batch requests"
cat <<'EOF' | dotnet run --project samples/Calculator 2>/dev/null
{"jsonrpc": "2.0", "id": 10, "method": "add", "params": {"a": 1, "b": 1}}
{"jsonrpc": "2.0", "id": 11, "method": "multiply", "params": {"a": 2, "b": 3}}
{"jsonrpc": "2.0", "id": 12, "method": "subtract", "params": {"a": 10, "b": 5}}
EOF
echo ""

echo "=== All manual tests completed ==="
