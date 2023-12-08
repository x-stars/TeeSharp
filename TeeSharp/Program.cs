var cmdOpts = CommandOptions.Parse(args);
if (cmdOpts.InvalidOption != null)
{
    foreach (var line in GetInvalidOptionMessage(cmdOpts.InvalidOption))
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

var stdin = Console.OpenStandardInput();
var stdout = Console.OpenStandardOutput();
var fileMode = cmdOpts.Append ? FileMode.Append : FileMode.Create;
var streams = Array.ConvertAll(cmdOpts.Files, file => (file == "-") ? stdout :
    new FileStream(file, fileMode, FileAccess.Write, FileShare.ReadWrite));

const int bufferSize = 4096;
var readBuffer = new byte[bufferSize];
var writeBuffer = new byte[bufferSize];
var stdoutTask = Task.CompletedTask;
var streamTasks = Array.ConvertAll(streams, stream => Task.CompletedTask);

var length = 0;
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

foreach (var stream in streams)
{
    stream.Dispose();
}
stdout.Dispose();
stdin.Dispose();
return 0;

static IEnumerable<string> GetHelpMessage()
{
    var cmdName = Program.GetCommandName();
    // Don't use the multi-line string literal to avoid hard-coding the newline sequence.
    yield return $"Usage: {cmdName} [OPTION]... [FILE]...";
    yield return $"Copy standard input to each FILE, and also to standard output.";
    yield return "";
    yield return "    -a, --append         Append to the given FILEs, do not overwrite.";
    yield return "    -?, -h, --help       Display this help and exit.";
    yield return "";
    yield return "When FILE is -, copy again to standard output.";
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

readonly record struct CommandOptions(bool Help, bool Append, string[] Files)
{
    public string? InvalidOption { get; init; }

    public static CommandOptions Parse(string[] args)
    {
        var help = false;
        var append = false;
        var files = new List<string>(args.Length);
        var invalidOpt = default(string);
        foreach (var index in ..args.Length)
        {
            var arg = args[index];
            switch (arg)
            {
                case "-?" or "-h" or "--help":
                    help = true;
                    break;
                case "-a" or "--append":
                    append = true;
                    break;
                case "--":
                    files.AddRange(args[(index + 1)..]);
                    goto ParseEnd;
                case { Length: > 1 } when arg.StartsWith('-'):
                    invalidOpt = arg;
                    goto ParseEnd;
                default:
                    files.Add(arg);
                    break;
            }
        }
    ParseEnd:
        return new(help, append, files.ToArray()) { InvalidOption = invalidOpt };
    }
}
