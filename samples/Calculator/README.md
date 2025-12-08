# PipeMux.Calculator

A simple calculator service that implements JSON-RPC 2.0 over stdin/stdout for testing the PipeMux.Broker communication loop.

## Features

- **JSON-RPC 2.0 Protocol**: Full compliance with JSON-RPC 2.0 specification
- **Four Basic Operations**: Add, Subtract, Multiply, Divide
- **Error Handling**: Proper error codes for division by zero, invalid methods, and invalid parameters
- **Stdin/Stdout Communication**: Reads JSON-RPC requests from stdin, outputs responses to stdout
- **Logging**: Diagnostic logs sent to stderr (doesn't interfere with JSON-RPC protocol)

## Supported Methods

### 1. `add`
Adds two numbers.

**Request:**
```json
{"jsonrpc": "2.0", "id": 1, "method": "add", "params": {"a": 5, "b": 3}}
```

**Response:**
```json
{"jsonrpc": "2.0", "id": 1, "result": 8}
```

### 2. `subtract`
Subtracts second number from first.

**Request:**
```json
{"jsonrpc": "2.0", "id": 2, "method": "subtract", "params": {"a": 10, "b": 4}}
```

**Response:**
```json
{"jsonrpc": "2.0", "id": 2, "result": 6}
```

### 3. `multiply`
Multiplies two numbers.

**Request:**
```json
{"jsonrpc": "2.0", "id": 3, "method": "multiply", "params": {"a": 6, "b": 7}}
```

**Response:**
```json
{"jsonrpc": "2.0", "id": 3, "result": 42}
```

### 4. `divide`
Divides first number by second.

**Request:**
```json
{"jsonrpc": "2.0", "id": 4, "method": "divide", "params": {"a": 20, "b": 4}}
```

**Response:**
```json
{"jsonrpc": "2.0", "id": 4, "result": 5}
```

**Error (Division by Zero):**
```json
{"jsonrpc": "2.0", "id": 5, "error": {"code": -32000, "message": "Division by zero"}}
```

## Error Codes

The calculator implements standard JSON-RPC 2.0 error codes:

| Code    | Message          | Description                              |
|---------|------------------|------------------------------------------|
| -32700  | Parse error      | Invalid JSON received                    |
| -32600  | Invalid Request  | Missing required fields                  |
| -32601  | Method not found | Unknown method name                      |
| -32602  | Invalid params   | Missing or invalid parameters            |
| -32000  | Server error     | Application error (e.g., division by zero)|

## Usage

### Build
```bash
dotnet build src/PipeMux.Calculator
```

### Run
```bash
# Echo a single request
echo '{"jsonrpc": "2.0", "id": 1, "method": "add", "params": {"a": 5, "b": 3}}' | \
    dotnet run --project src/PipeMux.Calculator

# Run interactively (type JSON requests, press Enter)
dotnet run --project src/PipeMux.Calculator

# Batch processing
cat requests.json | dotnet run --project src/PipeMux.Calculator
```

### Manual Testing
Run the included test script:
```bash
./src/PipeMux.Calculator/test-calculator.sh
```

### Unit Tests
```bash
dotnet test tests/PipeMux.Calculator.Tests
```

## Architecture

### Project Structure
```
src/PipeMux.Calculator/
├── PipeMux.Calculator.csproj      # Executable project
├── Program.cs                   # Main entry point (stdin/stdout loop)
├── CalculatorService.cs         # JSON-RPC request handler
├── test-calculator.sh           # Manual test script
└── README.md                    # This file
```

### Dependencies
- **PipeMux.Shared**: Provides JSON-RPC protocol types (`JsonRpcRequest`, `JsonRpcResponse`, `JsonRpcError`)
- **System.Text.Json**: JSON serialization/deserialization

### Communication Flow
1. **Input**: JSON-RPC request from stdin (one per line)
2. **Processing**: 
   - Parse JSON-RPC request
   - Route to appropriate method handler
   - Execute calculation
   - Handle errors (division by zero, invalid params, etc.)
3. **Output**: JSON-RPC response to stdout

### Logging
- All diagnostic logs go to **stderr**
- This ensures **stdout** contains only valid JSON-RPC responses
- Logs can be suppressed: `dotnet run --project src/PipeMux.Calculator 2>/dev/null`

## Examples

### Success Case
```bash
$ echo '{"jsonrpc": "2.0", "id": 1, "method": "add", "params": {"a": 5, "b": 3}}' | \
    dotnet run --project src/PipeMux.Calculator 2>/dev/null
{"jsonrpc":"2.0","id":1,"result":8,"error":null}
```

### Error Case (Division by Zero)
```bash
$ echo '{"jsonrpc": "2.0", "id": 5, "method": "divide", "params": {"a": 10, "b": 0}}' | \
    dotnet run --project src/PipeMux.Calculator 2>/dev/null
{"jsonrpc":"2.0","id":5,"result":null,"error":{"code":-32000,"message":"Division by zero","data":null}}
```

### Error Case (Unknown Method)
```bash
$ echo '{"jsonrpc": "2.0", "id": 6, "method": "unknown", "params": {"a": 1, "b": 2}}' | \
    dotnet run --project src/PipeMux.Calculator 2>/dev/null
{"jsonrpc":"2.0","id":6,"result":null,"error":{"code":-32601,"message":"Method not found","data":"unknown"}}
```

## Testing

The project includes comprehensive unit tests covering:
- ✅ 4 success scenarios (add, subtract, multiply, divide)
- ✅ Division by zero error
- ✅ Unknown method error
- ✅ Null parameters error
- ✅ Invalid parameters error
- ✅ Negative numbers
- ✅ Decimal numbers

Run tests:
```bash
dotnet test tests/PipeMux.Calculator.Tests
```

## Integration with PipeMux.Broker

This calculator serves as a test application for the PipeMux.Broker communication loop:

1. **Broker** receives user command (e.g., "calculate 5 + 3")
2. **Broker** launches Calculator process
3. **Broker** sends JSON-RPC request to Calculator's stdin
4. **Calculator** processes request and sends response to stdout
5. **Broker** receives response and formats it for the user

## License

Part of the PieceTreeSharp project.
