open System
open System.IO
open System.Threading.Tasks

let files = Environment.GetCommandLineArgs()[1..]
let stdin = Console.OpenStandardInput()
let stdout = Console.OpenStandardOutput()
let streams = files |> Array.map (fun file -> new FileStream(
    file, FileMode.Create, FileAccess.Write, FileShare.ReadWrite))

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
