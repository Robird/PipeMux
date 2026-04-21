// Calculator sample using PipeMux.Sdk with System.CommandLine
// Demonstrates stateful RPN (Reverse Polish Notation) calculator
// 
// The stack persists across requests (stateful service demo)
// Each operation outputs the current stack state
//
// Usage:
//   pmux calculator push 10    → Stack: [10]
//   pmux calculator push 20    → Stack: [10, 20]
//   pmux calculator add        → Stack: [30]
//   pmux calculator push 5     → Stack: [30, 5]
//   pmux calculator mul        → Stack: [150]
//   pmux calculator clear      → Stack: []

using System.CommandLine;
using PipeMux.Sdk;

// 创建有状态的计算器服务
var calculator = new StackCalculator();

// 创建 PipeMux App
var app = new PipeMuxApp("calculator");

var pushValue = new Argument<double>("value") { Description = "Value to push onto stack" };
var pushCommand = CreateValueCommand("push", "Push a value onto the stack", pushValue, calculator.Push);
var popCommand = CreateCommand("pop", "Pop and discard the top value", () => { calculator.Pop(); });
var dupCommand = CreateCommand("dup", "Duplicate the top value", calculator.Dup);
var swapCommand = CreateCommand("swap", "Swap the top two values", calculator.Swap);
var clearCommand = CreateCommand("clear", "Clear the stack", calculator.Clear);
var peekCommand = CreateCommand("peek", "Show current stack without modifying", () => { });
var addCommand = CreateCommand("add", "Pop two values, push their sum", calculator.Add);
var subCommand = CreateCommand("sub", "Pop two values (a, b), push a - b", calculator.Sub);
var mulCommand = CreateCommand("mul", "Pop two values, push their product", calculator.Mul);
var divCommand = CreateCommand("div", "Pop two values (a, b), push a / b", calculator.Div);
var negCommand = CreateCommand("neg", "Negate the top value", calculator.Neg);

// 定义根命令并添加所有子命令
var rootCommand = new RootCommand("RPN Calculator - A stateful stack-based calculator") {
    // 栈操作
    pushCommand,
    popCommand,
    dupCommand,
    swapCommand,
    clearCommand,
    peekCommand,
    // 算术运算
    addCommand,
    subCommand,
    mulCommand,
    divCommand,
    negCommand
};

// 运行 App
await app.RunAsync(rootCommand);

Command CreateCommand(string name, string description, Action action) {
    var command = new Command(name, description);
    command.SetAction(parseResult => Execute(parseResult, action));
    return command;
}

Command CreateValueCommand<T>(string name, string description, Argument<T> argument, Action<T> action) where T : notnull {
    var command = new Command(name, description) { argument };
    command.SetAction(parseResult => {
        var value = parseResult.GetValue(argument);
        return Execute(parseResult, () => action(value!));
    });
    return command;
}

int Execute(ParseResult parseResult, Action action) {
    try {
        action();
        parseResult.InvocationConfiguration.Output.WriteLine(calculator.FormatStack());
        return 0;
    }
    catch (Exception ex) when (ex is InvalidOperationException or DivideByZeroException) {
        parseResult.InvocationConfiguration.Error.WriteLine($"Error: {ex.Message}");
        return 1;
    }
}

// === 计算器服务类 ===

/// <summary>
/// 有状态的栈式计算器
/// </summary>
class StackCalculator {
    private readonly Stack<double> _stack = new();

    // 栈操作
    public void Push(double value) => _stack.Push(value);
    
    public double Pop() {
        if (_stack.Count == 0)
            throw new InvalidOperationException("Stack is empty");
        return _stack.Pop();
    }

    public void Dup() {
        if (_stack.Count == 0)
            throw new InvalidOperationException("Stack is empty");
        _stack.Push(_stack.Peek());
    }

    public void Swap() {
        if (_stack.Count < 2)
            throw new InvalidOperationException("Need at least 2 values");
        var b = _stack.Pop();
        var a = _stack.Pop();
        _stack.Push(b);
        _stack.Push(a);
    }

    public void Clear() => _stack.Clear();

    // 算术运算（从栈取操作数，结果入栈）
    public void Add() {
        var (a, b) = PopTwo();
        _stack.Push(a + b);
    }

    public void Sub() {
        var (a, b) = PopTwo();
        _stack.Push(a - b);
    }

    public void Mul() {
        var (a, b) = PopTwo();
        _stack.Push(a * b);
    }

    public void Div() {
        var (a, b) = PopTwo();
        if (b == 0)
            throw new DivideByZeroException("Division by zero");
        _stack.Push(a / b);
    }

    public void Neg() {
        if (_stack.Count == 0)
            throw new InvalidOperationException("Stack is empty");
        _stack.Push(-_stack.Pop());
    }

    // 辅助方法
    private (double a, double b) PopTwo() {
        if (_stack.Count < 2)
            throw new InvalidOperationException("Need at least 2 values");
        var b = _stack.Pop();
        var a = _stack.Pop();
        return (a, b);
    }

    /// <summary>
    /// 格式化栈状态用于输出
    /// </summary>
    public string FormatStack() {
        if (_stack.Count == 0)
            return "Stack: []";
        
        // 栈底在左，栈顶在右
        var items = _stack.Reverse().Select(FormatNumber);
        return $"Stack: [{string.Join(", ", items)}]";
    }

    private static string FormatNumber(double n) {
        // 整数显示为整数，小数保留合理精度
        return n == Math.Floor(n) ? ((long)n).ToString() : n.ToString("G10");
    }
}
