using JiggleSharp.Core.Hosting;

namespace JiggleSharp.Mac.Logging;

public class MacSystemLog : ILogger
{
    public void Info(string message)
    {
        Console.WriteLine(message);
    }

    public void Error(string message, Exception? ex = null)
    {
        Console.WriteLine(message);
    }

    public void Warning(string message)
    {
        Console.WriteLine(message);
    }
}