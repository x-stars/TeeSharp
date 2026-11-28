open System
open System.IO
open System.Threading.Tasks

[<Struct; NoEquality; NoComparison>]
type CommandOptions =
    {
        Help: bool
        Append: bool
        BufferSize: int
        Files: string list
    }

    // Disable the auto-generated Printf-based ToString() when reflection disabled.
    override _.ToString() = nameof CommandOptions

    static member TryParse(args: string[]) =
        let rec tryParseRest args result =
            match args with
            | [] -> Ok { result with Files = List.rev result.Files }
            | ("-?" | "-h" | "--help") :: rest ->
                tryParseRest rest { result with Help = true }
            | ("-a" | "--append") :: rest ->
                tryParseRest rest { result with Append = true }
            | ("-b" | "--buffer-size") as arg :: nextArg :: rest ->
                match Int32.TryParse(nextArg) with
                | true, value when value > 0 ->
                    tryParseRest rest { result with BufferSize = value }
                | _, _ -> Error (arg + " " + nextArg)
            | "--" :: rest ->
                Ok { result with Files = (List.rev result.Files) @ rest }
            | arg :: _ when (arg.Length > 1) && (arg.[0] = '-') -> Error arg
            | arg :: rest ->
                tryParseRest rest { result with Files = arg :: result.Files }
        tryParseRest (args |> Array.toList) {
            Help = false; Append = false; BufferSize = 4096; Files = []
        }

let commandName =
    let cmdPath = Environment.GetCommandLineArgs().[0]
    let cmdName = Path.GetFileNameWithoutExtension(cmdPath)
    let cmdExt = Path.GetExtension(cmdPath)
    let hasPathExt = OperatingSystem.IsWindows()
    if hasPathExt && (cmdExt.Length > 0)
        then $"{cmdName}[{cmdExt}]" else Path.GetFileName(cmdPath)

let helpMessage = seq {
    // Don't use multi-line string literals to avoid hard-coding the newline sequence.
    $"Usage: {commandName} [OPTION]... [FILE]..."
    $"Copy standard input to each FILE, and also to standard output."
    ""
    "    -a, --append            Append to the given FILEs, do not overwrite."
    "    -b, --buffer-size N     Buffer size N using in copying, default to 4096."
    "    -?, -h, --help          Display this help and exit."
    ""
    "If a FILE is -, copy again to standard output."
}

let invalidOptMessage (option: string) = seq {
    $"Invalid option: {option}"
    $"Try '{commandName} --help' for more information."
}

let tryOpenFile (stdout: Stream) append file =
    try
        let fileMode = if append then FileMode.Append else FileMode.Create
        // Don't use the Windows Overlapped I/O on local files to avoid overhead.
        Ok (if file = "-" then stdout else new FileStream(
            file, fileMode, FileAccess.Write, FileShare.ReadWrite, bufferSize = 1))
    with ex ->
        Error $"{ex.GetType()}: {ex.Message}"

let openStreams cmdOpts =
    let stdin = Console.OpenStandardInput()
    let stdout = Console.OpenStandardOutput()
    let files = cmdOpts.Files |> List.toArray
    let streamResults = files |> Array.map (tryOpenFile stdout cmdOpts.Append)
    let streams = streamResults |> Array.map (Result.defaultValue Stream.Null)
    let getError = function | Ok _ -> null | Error value -> value
    let errors = streamResults |> Seq.map getError |> Seq.filter (not << isNull)
    struct (stdin, stdout, streams, errors)

let rec copyInput (stdin: Stream, stdout: Stream, streams: Stream[])
                  (buffer: byte[], lastBuffer: byte[])
                  (lastStdoutTask: Task, lastStreamTasks: Task[]) =
    let readWriteTask = task {
        let! length = stdin.ReadAsync(buffer)
        do! lastStdoutTask
        do! Task.WhenAll(lastStreamTasks)
        return length
    } // Use sync wait to write in tail-rec style. Safe for the last step.
    match readWriteTask.GetAwaiter().GetResult() with
    | 0 -> ()
    | length ->
        let streamTasks = streams |> Array.map _.WriteAsync(buffer, 0, length)
        let stdoutTask = stdout.WriteAsync(buffer, 0, length)
        copyInput (stdin, stdout, streams) (lastBuffer, buffer) (stdoutTask, streamTasks)

[<EntryPoint>]
let main args =
    match CommandOptions.TryParse(args) with
    | Error invalidOpt ->
        invalidOptMessage invalidOpt |> Seq.iter stderr.WriteLine
        1
    | Ok cmdOpts when cmdOpts.Help ->
        // Don't use the Printf module when reflection disabled.
        helpMessage |> Seq.iter stdout.WriteLine
        0
    | Ok cmdOpts ->
        let struct (stdin, stdout, streams, errors) = openStreams cmdOpts
        errors |> Seq.iter stderr.WriteLine
        use _ = stdin
        use _ = stdout
        use _ = { new IDisposable with
            member _.Dispose() = streams |> Array.iter _.Dispose()
        }
        copyInput (stdin, stdout, streams)
            (Array.zeroCreate cmdOpts.BufferSize, Array.zeroCreate cmdOpts.BufferSize)
            (Task.CompletedTask, streams |> Array.map (fun _ -> Task.CompletedTask))
        if Seq.isEmpty errors then 0 else 2
