// Calculator sample using PipeMux.Sdk with System.CommandLine
// Demonstrates stateful RPN (Reverse Polish Notation) calculator
// 
// The stack persists across requests (stateful service demo)
// Each operation outputs the current stack state
//
// Usage:
//   pipemux calc push 10    → Stack: [10]
//   pipemux calc push 20    → Stack: [10, 20]
//   pipemux calc add        → Stack: [30]
//   pipemux calc push 5     → Stack: [30, 5]
//   pipemux calc mul        → Stack: [150]
//   pipemux calc clear      → Stack: []

using System.CommandLine;
using PipeMux.Sdk;

// 创建有状态的计算器服务
var calculator = new StackCalculator();

// 创建 PipeMux App
var app = new PipeMuxApp("calculator");

// === 栈操作命令 ===

// push - 将数值压入栈
var pushValue = new Argument<double>("value") { Description = "Value to push onto stack" };
var pushCommand = new Command("push", "Push a value onto the stack") { pushValue };
pushCommand.SetAction(parseResult => {
    var value = parseResult.GetValue(pushValue);
    calculator.Push(value);
    parseResult.Configuration.Output.WriteLine(calculator.FormatStack());
});

// pop - 弹出栈顶
var popCommand = new Command("pop", "Pop and discard the top value");
popCommand.SetAction(parseResult => {
    try {
        calculator.Pop();
        parseResult.Configuration.Output.WriteLine(calculator.FormatStack());
    }
    catch (InvalidOperationException ex) {
        parseResult.Configuration.Error.WriteLine($"Error: {ex.Message}");
        return 1;
    }
    return 0;
});

// dup - 复制栈顶
var dupCommand = new Command("dup", "Duplicate the top value");
dupCommand.SetAction(parseResult => {
    try {
        calculator.Dup();
        parseResult.Configuration.Output.WriteLine(calculator.FormatStack());
    }
    catch (InvalidOperationException ex) {
        parseResult.Configuration.Error.WriteLine($"Error: {ex.Message}");
        return 1;
    }
    return 0;
});

// swap - 交换栈顶两个值
var swapCommand = new Command("swap", "Swap the top two values");
swapCommand.SetAction(parseResult => {
    try {
        calculator.Swap();
        parseResult.Configuration.Output.WriteLine(calculator.FormatStack());
    }
    catch (InvalidOperationException ex) {
        parseResult.Configuration.Error.WriteLine($"Error: {ex.Message}");
        return 1;
    }
    return 0;
});

// clear - 清空栈
var clearCommand = new Command("clear", "Clear the stack");
clearCommand.SetAction(parseResult => {
    calculator.Clear();
    parseResult.Configuration.Output.WriteLine(calculator.FormatStack());
});

// peek - 查看栈状态（不修改）
var peekCommand = new Command("peek", "Show current stack without modifying");
peekCommand.SetAction(parseResult => {
    parseResult.Configuration.Output.WriteLine(calculator.FormatStack());
});

// === 算术运算命令 ===

// add - 加法
var addCommand = new Command("add", "Pop two values, push their sum");
addCommand.SetAction(parseResult => {
    try {
        calculator.Add();
        parseResult.Configuration.Output.WriteLine(calculator.FormatStack());
    }
    catch (InvalidOperationException ex) {
        parseResult.Configuration.Error.WriteLine($"Error: {ex.Message}");
        return 1;
    }
    return 0;
});

// sub - 减法
var subCommand = new Command("sub", "Pop two values (a, b), push a - b");
subCommand.SetAction(parseResult => {
    try {
        calculator.Sub();
        parseResult.Configuration.Output.WriteLine(calculator.FormatStack());
    }
    catch (InvalidOperationException ex) {
        parseResult.Configuration.Error.WriteLine($"Error: {ex.Message}");
        return 1;
    }
    return 0;
});

// mul - 乘法
var mulCommand = new Command("mul", "Pop two values, push their product");
mulCommand.SetAction(parseResult => {
    try {
        calculator.Mul();
        parseResult.Configuration.Output.WriteLine(calculator.FormatStack());
    }
    catch (InvalidOperationException ex) {
        parseResult.Configuration.Error.WriteLine($"Error: {ex.Message}");
        return 1;
    }
    return 0;
});

// div - 除法
var divCommand = new Command("div", "Pop two values (a, b), push a / b");
divCommand.SetAction(parseResult => {
    try {
        calculator.Div();
        parseResult.Configuration.Output.WriteLine(calculator.FormatStack());
    }
    catch (InvalidOperationException ex) {
        parseResult.Configuration.Error.WriteLine($"Error: {ex.Message}");
        return 1;
    }
    catch (DivideByZeroException) {
        parseResult.Configuration.Error.WriteLine("Error: Division by zero");
        return 1;
    }
    return 0;
});

// neg - 取反
var negCommand = new Command("neg", "Negate the top value");
negCommand.SetAction(parseResult => {
    try {
        calculator.Neg();
        parseResult.Configuration.Output.WriteLine(calculator.FormatStack());
    }
    catch (InvalidOperationException ex) {
        parseResult.Configuration.Error.WriteLine($"Error: {ex.Message}");
        return 1;
    }
    return 0;
});

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
            throw new DivideByZeroException();
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
