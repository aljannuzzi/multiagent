namespace Common;

using Microsoft.SemanticKernel;

#pragma warning disable SKEXP0001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
public class DebugFunctionFilter : IFunctionFilter
{
    public void OnFunctionInvoked(FunctionInvokedContext context) { }

    public void OnFunctionInvoking(FunctionInvokingContext context) { }
#pragma warning restore SKEXP0001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
}
