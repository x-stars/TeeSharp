open System
open System.IO
open System.Threading.Tasks

[<return: Struct>]
let inline (|PositiveInt32|_|) (text: string) =
    match Int32.TryParse(text) with
    | true, result when result > 0 -> ValueSome result
    | _, _ -> ValueNone

[<Struct; NoComparison>]
type CommandOptions =
    {
        Help: bool
        Append: bool
        BufferSize: int
        Files: string list
    }

    static member Parse(args: string[]) =
        let rec parseRest args result =
            match args with
            | [] -> Ok { result with Files = List.rev result.Files }
            | ("-?" | "-h" | "--help") :: rest ->
                parseRest rest { result with Help = true }
            | ("-a" | "--append") :: rest ->
                parseRest rest { result with Append = true }
            | ("-b" | "--buffer-size") :: PositiveInt32 value :: rest ->
                parseRest rest { result with BufferSize = value }
            | ("-b" | "--buffer-size") as arg :: nextArg :: _ ->
                Error (arg + " " + nextArg)
            | ("-b" | "--buffer-size") as arg :: [] -> Error arg
            | "--" :: rest ->
                Ok { result with Files = (List.rev result.Files) @ rest }
            | arg :: _ when arg.StartsWith('-') && (arg.Length > 1) -> Error arg
            | arg :: rest ->
                parseRest rest { result with Files = arg :: result.Files }
        parseRest (args |> Array.toList) {
            Help = false; Append = false
            BufferSize = 4096; Files = []
        }

let commandName =
    let cmdPath = Environment.GetCommandLineArgs()[0]
    let cmdName = Path.GetFileNameWithoutExtension(cmdPath)
    let cmdExt = Path.GetExtension(cmdPath)
    let hasPathExt = Environment.OSVersion.Platform < PlatformID.Unix
    if hasPathExt && (cmdExt.Length > 0)
        // Don't use interpolated strings when reflection disabled.
        then cmdName + "[" + cmdExt + "]" else Path.GetFileName(cmdPath)

let helpMessage = seq {
    // Don't use multi-line string literals to avoid hard-coding the newline sequence.
    "Usage: " + commandName + " [OPTION]... [FILE]..."
    "Copy standard input to each FILE, and also to standard output."
    ""
    "    -a, --append            Append to the given FILEs, do not overwrite."
    "    -b, --buffer-size N     Buffer size N using in copying, default to 4096."
    "    -?, -h, --help          Display this help and exit."
    ""
    "If a FILE is -, copy again to standard output."
}

let invalidOptMessage (option: string) = seq {
    "Invalid option: " + option
    "Try '" + commandName + " --help' for more information."
}

let args = Environment.GetCommandLineArgs()[1..]
let cmdOpts =
    match CommandOptions.Parse(args) with
    | Error invalidOpt ->
        invalidOptMessage invalidOpt
        |> Seq.iter Console.Error.WriteLine
        exit 1
    | Ok cmdOpts when cmdOpts.Help ->
        // Don't use the Printf module when reflection disabled.
        helpMessage |> Seq.iter Console.Out.WriteLine
        exit 0
    | Ok cmdOpts -> cmdOpts

let stdin = Console.OpenStandardInput()
let stdout = Console.OpenStandardOutput()
let files = cmdOpts.Files |> List.toArray
let fileMode = if cmdOpts.Append then FileMode.Append else FileMode.Create
let streams =
    try
        files |> Array.map (fun file ->
            if file = "-" then stdout else new FileStream(
                file, fileMode, FileAccess.Write, FileShare.ReadWrite))
    with
    | :? IOException as ex ->
        // Reflection disabled, unable to get the actual type name.
        Console.Error.WriteLine((nameof IOException) + ": " + ex.Message)
        exit 2
    | :? SystemException as ex ->
        Console.Error.WriteLine((nameof SystemException) + ": " + ex.Message)
        exit 2
AppDomain.CurrentDomain.ProcessExit.Add(fun __ ->
    streams |> Array.iter (_.Dispose())
    stdout.Dispose()
    stdin.Dispose()
)

let rec copyInput (buffer: byte[]) (lastBuffer: byte[])
                  (lastStdoutTask: Task) (lastStreamTasks: Task[]) =
    let lengthTask = task {
        let! length = stdin.ReadAsync(buffer)
        let! _ = lastStdoutTask
        let! _ = Task.WhenAll(lastStreamTasks)
        return length
    }
    match lengthTask.Result with
    | 0 -> ()
    | length ->
        let streamTasks = streams |> Array.map _.WriteAsync(buffer, 0, length)
        let stdoutTask = stdout.WriteAsync(buffer, 0, length)
        copyInput lastBuffer buffer stdoutTask streamTasks

let bufferSize = cmdOpts.BufferSize
copyInput (Array.zeroCreate bufferSize) (Array.zeroCreate bufferSize)
          (Task.CompletedTask) (streams |> Array.map (fun _ -> Task.CompletedTask))
exit 0
