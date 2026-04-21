#!/bin/bash

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"
TEST_PROJECT="$ROOT_DIR/tests/ManagementCommandParseTests/ManagementCommandParseTests.csproj"

echo "======================================"
echo "PipeMux ManagementCommand Parse Test"
echo "======================================"
echo ""

dotnet run --project "$TEST_PROJECT" --nologo
