var cmdOpts = default(CommandOptions);
try
{
    cmdOpts = CommandOptions.Parse(args);
}
catch (ArgumentOutOfRangeException ex)
{
    foreach (var line in GetInvalidOptionMessage(ex.ParamName!))
    {
        Console.Error.WriteLine(line);
    }
    return 1;
}
if (cmdOpts.Help)
{
    foreach (var line in GetHelpMessage())
    {
        Console.Out.WriteLine(line);
    }
    return 0;
}

using var stdin = Console.OpenStandardInput();
using var stdout = Console.OpenStandardOutput();
var fileMode = cmdOpts.Append ? FileMode.Append : FileMode.Create;
var streams = Array.Empty<Stream>();
try
{
    streams = Array.ConvertAll(cmdOpts.Files.ToArray(),
        file => (file == "-") ? stdout : new FileStream(
            file, fileMode, FileAccess.Write, FileShare.ReadWrite));
}
catch (IOException ex)
{
    // Reflection disabled, unable to get the actual type name.
    Console.Error.WriteLine($"{nameof(IOException)}: {ex.Message}");
    return 2;
}
catch (SystemException ex)
{
    Console.Error.WriteLine($"{nameof(SystemException)}: {ex.Message}");
    return 2;
}
using var streamsDisposable = new DisposeAction(() =>
{
    foreach (var stream in streams)
    {
        stream?.Dispose();
    }
});

var length = 0;
var readBuffer = new byte[cmdOpts.BufferSize];
var writeBuffer = new byte[cmdOpts.BufferSize];
var stdoutTask = Task.CompletedTask;
var streamTasks = Array.ConvertAll(streams, stream => Task.CompletedTask);
while ((length = await stdin.ReadAsync(readBuffer)) != 0)
{
    await stdoutTask;
    await Task.WhenAll(streamTasks);
    (writeBuffer, readBuffer) = (readBuffer, writeBuffer);
    foreach (var index in ..streams.Length)
    {
        streamTasks[index] = streams[index].WriteAsync(writeBuffer, 0, length);
    }
    stdoutTask = stdout.WriteAsync(writeBuffer, 0, length);
}
await stdoutTask;
await Task.WhenAll(streamTasks);
return 0;

static IEnumerable<string> GetHelpMessage()
{
    var cmdName = Program.GetCommandName();
    // Don't use multi-line string literals to avoid hard-coding the newline sequence.
    yield return $"Usage: {cmdName} [OPTION]... [FILE]...";
    yield return $"Copy standard input to each FILE, and also to standard output.";
    yield return "";
    yield return "    -a, --append            Append to the given FILEs, do not overwrite.";
    yield return "    -b, --buffer-size N     Buffer size N using in copying, default to 4096.";
    yield return "    -?, -h, --help          Display this help and exit.";
    yield return "";
    yield return "If a FILE is -, copy again to standard output.";
}

static IEnumerable<string> GetInvalidOptionMessage(string option)
{
    var cmdName = Program.GetCommandName();
    yield return $"Invalid option: {option}";
    yield return $"Try '{cmdName} --help' for more information.";
}

static partial class Program
{
    internal static string GetCommandName()
    {
        var cmdPath = Environment.GetCommandLineArgs()[0];
        var cmdName = Path.GetFileNameWithoutExtension(cmdPath);
        var cmdExt = Path.GetExtension(cmdPath);
        var hasPathExt = Environment.OSVersion.Platform < PlatformID.Unix;
        return (hasPathExt && (cmdExt.Length > 0)) ?
            $"{cmdName}[{cmdExt}]" : Path.GetFileName(cmdPath);
    }
}

readonly record struct CommandOptions(bool Help, bool Append, int BufferSize, List<string> Files)
{
    public CommandOptions WithAddingFile(string file)
    {
        this.Files.Add(file);
        return this;
    }

    public CommandOptions WithAddingFiles(ReadOnlySpan<string> files)
    {
        this.Files.AddRange(files);
        return this;
    }

    public static CommandOptions Parse(string[] args)
    {
        static CommandOptions ParseNext(ReadOnlySpan<string> args, CommandOptions result)
        {
            return args switch
            {
                [] => result,
                ["-?" or "-h" or "--help", .. var rest] =>
                    ParseNext(rest, result with { Help = true }),
                ["-a" or "--append", .. var rest] =>
                    ParseNext(rest, result with { Append = true }),
                ["-b" or "--buffer-size", var nextArg, .. var rest]
                when int.TryParse(nextArg, out var value) && value > 0 =>
                    ParseNext(rest, result with { BufferSize = value }),
                [("-b" or "--buffer-size") and var arg, var nextArg, ..] =>
                    throw new ArgumentOutOfRangeException($"{arg} {nextArg}"),
                [("-b" or "--buffer-size") and var arg] =>
                    throw new ArgumentOutOfRangeException(arg),
                ["--", .. var rest] => result.WithAddingFiles(rest),
                [{ Length: > 1 } arg, ..] when arg.StartsWith('-') =>
                    throw new ArgumentOutOfRangeException(arg),
                [var arg, .. var rest] =>
                    ParseNext(rest, result.WithAddingFile(arg)),
            };
        }
        return ParseNext(args, new() { BufferSize = 4096, Files = new(args.Length) });
    }
}

sealed class DisposeAction(Action action) : IDisposable
{
    public void Dispose() { action.Invoke(); }
}
