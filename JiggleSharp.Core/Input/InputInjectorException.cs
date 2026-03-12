namespace JiggleSharp.Core.Input;

public class InputInjectorException : Exception
{
    public InputInjectorException() {}
    public InputInjectorException(string message) : base(message) {}
    public InputInjectorException(string message, Exception inner) : base(message, inner) {}
}