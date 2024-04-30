namespace Common;

using Microsoft.SemanticKernel;

#pragma warning disable SKEXP0001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
public class LogFunctionInvocationFilter(Func<(string Message, string? OptionalResult), Task> beforeLogMethod, Func<(string Message, string? OptionalResult), Task> afterLogMethod, bool includeResult = false) : IFunctionFilter
{
    private readonly Func<(string Message, string? OptionalResult), Task> _beforeLogMethod = Throws.IfNull(beforeLogMethod);
    private readonly Func<(string Message, string? OptionalResult), Task> _afterLogMethod = Throws.IfNull(afterLogMethod);
    private readonly bool _includeResult = includeResult;

    public LogFunctionInvocationFilter(Action<(string Message, string? OptionalResult)> logMethod, bool includeResult = false) : this(logMethod, logMethod, includeResult) { }

    public LogFunctionInvocationFilter(Action<(string Message, string? OptionalResult)> beforeLogMethod, Action<(string Message, string? OptionalResult)> afterLogMethod, bool includeResult = false) : this(s => Task.Run(() => beforeLogMethod(s)), s => Task.Run(() => afterLogMethod(s)), includeResult) { }

    public LogFunctionInvocationFilter(Func<(string Message, string? OptionalResult), Task> logMethod, bool includeResult = false) : this(logMethod, logMethod, includeResult) { }

    public async void OnFunctionInvoked(FunctionInvokedContext context) => await _afterLogMethod.Invoke(($"{context.Function.Name} completed.", _includeResult ? $@" Result: {context.Result}" : null));

    public async void OnFunctionInvoking(FunctionInvokingContext context) => await _beforeLogMethod.Invoke(($"Running {context.Function.Name} ({context.Function.Description}) ...", null));
#pragma warning restore SKEXP0001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
}
