using JiggleSharp.Core.Hosting;

namespace JiggleSharp.Linux.Logging;

public class LinuxSystemLog : ILogger
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