namespace Common;

using Microsoft.SemanticKernel;

#pragma warning disable SKEXP0001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
public class DebugFunctionFilter/*(ILoggerFactory loggerFactory)*/ : IFunctionFilter
{
    //private readonly ILogger _log = loggerFactory.CreateLogger("Microsoft.SemanticKernel.DebugFunctionFilter");

    public void OnFunctionInvoked(FunctionInvokedContext context)
    {
        //_log.LogTrace("Function result: {functionResult}", context.Result.ToString());
    }

    public void OnFunctionInvoking(FunctionInvokingContext context) { }
#pragma warning restore SKEXP0001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
}
