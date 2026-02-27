using System;
using System.Collections.Generic;
using System.Text;

namespace Ollang.Errors
{
    public class StackTraceEntry
    {
        public string FunctionName { get; }
        public int InstructionPointer { get; }
        public int Line { get; }
        public int Column { get; }

        public StackTraceEntry(string funcName, int ip, int line = 0, int col = 0)
        {
            FunctionName = funcName;
            InstructionPointer = ip;
            Line = line;
            Column = col;
        }

        public override string ToString()
        {
            if (Line > 0) return $"  at {FunctionName} (line {Line}:{Column})";
            return $"  at {FunctionName} [ip:{InstructionPointer}]";
        }
    }

    public class OllangException : Exception
    {
        public string ErrorType { get; }
        public int SourceLine { get; }
        public int SourceColumn { get; }
        public string? FunctionName { get; }
        public List<StackTraceEntry> CallStack { get; } = new();
        public string? Hint { get; set; }

        public OllangException(string type, string message, int line = 0, int col = 0, string? function = null)
            : base(message)
        {
            ErrorType = type;
            SourceLine = line;
            SourceColumn = col;
            FunctionName = function;
        }

        public OllangException(string type, string message, Exception inner, int line = 0, int col = 0)
            : base(message, inner)
        {
            ErrorType = type;
            SourceLine = line;
            SourceColumn = col;
        }

        public string FormatError()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"[{ErrorType}] {Message}");

            if (SourceLine > 0)
                sb.AppendLine($"  Location: line {SourceLine}, column {SourceColumn}");

            if (FunctionName != null)
                sb.AppendLine($"  Function: {FunctionName}");

            if (CallStack.Count > 0)
            {
                sb.AppendLine("  Stack trace:");
                for (int i = CallStack.Count - 1; i >= 0; i--)
                    sb.AppendLine($"    {CallStack[i]}");
            }

            if (Hint != null)
                sb.AppendLine($"  Hint: {Hint}");

            return sb.ToString();
        }

        public override string ToString() => FormatError();
    }

    public static class ErrorHints
    {

        // thank you chatgpt for the error messages ✌️🥹
        public static string? GetHint(string errorMessage, string? context = null)
        {
            if (errorMessage.Contains("not defined"))
            {
                if (context != null)
                    return $"Did you forget to declare '{context}' with 'var'?";
                return "Check for typos in variable names or missing 'var' declarations.";
            }

            if (errorMessage.Contains("not found") && errorMessage.Contains("Global"))
                return "The variable may not have been imported or defined in the current scope.";

            if (errorMessage.Contains("non-callable"))
                return "You're trying to call something that isn't a function. Check the variable type.";

            if (errorMessage.Contains("Function") && errorMessage.Contains("not found"))
                return "Ensure the function is defined before it's called, or check for typos.";

            if (errorMessage.Contains("Index") && errorMessage.Contains("out of range"))
                return "Array or string index is out of bounds. Check the length before accessing.";

            if (errorMessage.Contains("division") || errorMessage.Contains("Infinity") || errorMessage.Contains("NaN"))
                return "Check for division by zero or invalid math operations.";

            if (errorMessage.Contains("type") && errorMessage.Contains("mismatch"))
                return "Ensure operand types match. Use str(), num(), or bool() for conversion.";

            if (errorMessage.Contains("Import") && errorMessage.Contains("resolve"))
                return "Check the file path and ensure the module exists in the stdlib directory.";

            if (errorMessage.Contains("Expected"))
                return "Check for missing brackets, parentheses, or semicolons.";

            if (errorMessage.Contains("Unexpected token"))
                return "There may be a syntax error nearby. Check for missing operators or delimiters.";

            if (errorMessage.Contains("system()") && errorMessage.Contains("disabled"))
                return "System calls are disabled by default for security. Use --allow-system flag.";

            return null;
        }
    }
}
