open System
open System.IO
open System.Threading.Tasks

[<Struct; NoComparison>]
type CommandOptions =
    {
        Help: bool
        Append: bool
        Files: string list
        InvalidOption: string
    }

    static member Parse(args: string[]) =
        let rec parseRest args result =
            match args with
            | ("-?" | "-h" | "--help") :: rest ->
                parseRest rest { result with Help = true }
            | ("-a" | "--append") :: rest ->
                parseRest rest { result with Append = true }
            | "--" :: rest ->
                { result with Files = (List.rev result.Files) @ rest }
            | arg :: _ when arg.StartsWith('-') && (args.Length > 1) ->
                { result with InvalidOption = arg }
            | arg :: rest ->
                parseRest rest { result with Files = arg :: result.Files }
            | [] -> { result with Files = List.rev result.Files }
        parseRest (args |> Array.toList) {
            Help = false; Append = true
            Files = []; InvalidOption = null
        }

let commandName =
    let cmdPath = Environment.GetCommandLineArgs()[0]
    let cmdName = Path.GetFileNameWithoutExtension(cmdPath)
    let cmdExt = Path.GetExtension(cmdPath)
    let hasPathExt = Environment.OSVersion.Platform < PlatformID.Unix
    if (hasPathExt && (cmdExt.Length > 0))
        // Don't use the interpolated string syntax when reflection disabled.
        then cmdName + "[" + cmdExt + "]" else Path.GetFileName(cmdPath)

let helpMessage = seq {
    // Don't use the multi-line string literal to avoid hard-coding the newline sequence.
    "Usage: " + commandName + " [OPTION]... [FILE]..."
    "Copy standard input to each FILE, and also to standard output."
    ""
    "    -a, --append            Append to the given FILEs, do not overwrite."
    "    -?, -h, --help          Display this help and exit."
    ""
    "When FILE is -, copy again to standard output."
}

let invalidOptMessage (option: string) = seq {
    "Invalid option: " + option
    "Try '" + commandName + " --help' for more information."
}

let args = Environment.GetCommandLineArgs()[1..]
let cmdOpts = CommandOptions.Parse(args)
if (not << isNull) cmdOpts.InvalidOption then
    invalidOptMessage cmdOpts.InvalidOption
    |> Seq.iter Console.Error.WriteLine
    exit 1
if cmdOpts.Help then
    // Don't use the Printf module when reflection disabled.
    helpMessage |> Seq.iter Console.Out.WriteLine
    exit 0

let stdin = Console.OpenStandardInput()
let stdout = Console.OpenStandardOutput()
let files = cmdOpts.Files |> List.toArray
let fileMode = if cmdOpts.Append then FileMode.Append else FileMode.Create
let streams = files |> Array.map (fun file ->
    if file = "-" then stdout else new FileStream(
        file, fileMode, FileAccess.Write, FileShare.ReadWrite))

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

let [<Literal>] bufferSize = 4096
copyInput (Array.zeroCreate bufferSize) (Array.zeroCreate bufferSize)
          (Task.CompletedTask) (streams |> Array.map (fun _ -> Task.CompletedTask))

streams |> Array.iter (_.Dispose())
stdout.Dispose()
stdin.Dispose()
