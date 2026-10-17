using UnityEngine;

// Batch-mode entry: XLua/Validate Setup, logging each problem.
public static class HotfixIntKeySteps
{
    public static void Validate()
    {
        var problems = CSObjectWrapEditor.SetupValidator.Validate();
        foreach (var problem in problems)
        {
            Debug.Log("XLUA_VALIDATE_PROBLEM: " + problem);
        }
        Debug.Log("XLUA_VALIDATE_DONE: " + problems.Count + " problems");
    }
}
