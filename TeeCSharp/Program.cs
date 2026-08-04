if (CommandOptions.TryParse(args, out var cmdOpts) is string error)
{
    Console.Error.WriteLines(GetInvalidOptionMessage(error));
    return 1;
}
if (cmdOpts.Help)
{
    Console.Out.WriteLines(GetHelpMessage());
    return 0;
}

using var stdin = Console.OpenStandardInput();
using var stdout = Console.OpenStandardOutput();
var streams = cmdOpts.Files.Select(
    file => OpenFileOrNull(file, cmdOpts.Append, stdout)).ToArray();
using var streamsDisposable = new DisposeAction(
    () => Array.ForEach(streams, stream => stream.Dispose()));

var length = 0;
var readBuffer = (new byte[cmdOpts.BufferSize]).AsMemory();
var writeBuffer = (new byte[cmdOpts.BufferSize]).AsMemory();
var stdoutTask = ValueTask.CompletedTask;
var streamTasks = Array.ConvertAll(streams, stream => ValueTask.CompletedTask);
while ((length = await stdin.ReadAsync(readBuffer)) != 0)
{
    await stdoutTask;
    foreach (var streamTask in streamTasks) { await streamTask; }
    (writeBuffer, readBuffer) = (readBuffer, writeBuffer);
    foreach (var index in ..streams.Length)
    {
        streamTasks[index] = streams[index].WriteAsync(writeBuffer[..length]);
    }
    stdoutTask = stdout.WriteAsync(writeBuffer[..length]);
}
await stdoutTask;
foreach (var streamTask in streamTasks) { await streamTask; }
return (Array.IndexOf(streams, Stream.Null) >= 0) ? 2 : 0;

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

static Stream OpenFileOrNull(string file, bool append, Stream stdout)
{
    try
    {
        var fileMode = append ? FileMode.Append : FileMode.Create;
        return (file == "-") ? stdout : new FileStream(
            file, fileMode, FileAccess.Write, FileShare.ReadWrite, bufferSize: 1);
    }
    catch (IOException ex)
    {
        // Reflection disabled, unable to get the actual type name.
        Console.Error.WriteLine($"{nameof(IOException)}: {ex.Message}");
        return Stream.Null;
    }
    catch (SystemException ex)
    {
        Console.Error.WriteLine($"{nameof(SystemException)}: {ex.Message}");
        return Stream.Null;
    }
}

static partial class Program
{
    internal static string GetCommandName()
    {
        var cmdPath = Environment.GetCommandLineArgs()[0].AsSpan();
        var cmdName = Path.GetFileNameWithoutExtension(cmdPath);
        var cmdExt = Path.GetExtension(cmdPath);
        var hasPathExt = Environment.OSVersion.Platform < PlatformID.Unix;
        return (hasPathExt && (cmdExt.Length > 0)) ?
            $"{cmdName}[{cmdExt}]" : Path.GetFileName(cmdPath).ToString();
    }

    internal static void WriteLines(this TextWriter writer, IEnumerable<string> lines)
    {
        foreach (var line in lines)
        {
            writer.WriteLine(line);
        }
    }
}

readonly record struct CommandOptions(bool Help, bool Append, int BufferSize, IEnumerable<string> Files)
{
    public static string? TryParse(string[] args, out CommandOptions result)
    {
        var error = (string?)null;
        var current = (ArraySegment<string>)args;
        result = new CommandOptions() { BufferSize = 4096, Files = [] };
        while (current is not [])
        {
            (current, result, error) = current switch
            {
                [] => ([], result, error),
                ["-?" or "-h" or "--help", .. var rest] =>
                    (rest, result with { Help = true }, error),
                ["-a" or "--append", .. var rest] =>
                    (rest, result with { Append = true }, error),
                [("-b" or "--buffer-size") and var arg, var nextArg, .. var rest] =>
                    (int.TryParse(nextArg, out var value) && value > 0) ?
                        (rest, result with { BufferSize = value }, error) :
                        ([], result, error: $"{arg} {nextArg}"),
                ["--", .. var rest] =>
                    ([], result with { Files = result.Files.Concat(rest) }, error),
                [['-', _, ..] arg, ..] => ([], result, error: arg),
                [var arg, .. var rest] =>
                    (rest, result with { Files = result.Files.Append(arg) }, error),
            };
        }
        return error;
    }
}

readonly struct DisposeAction(Action action) : IDisposable
{
    public void Dispose() => action.Invoke();
}
