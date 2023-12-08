var files = args;
var stdin = Console.OpenStandardInput();
var stdout = Console.OpenStandardOutput();
var streams = Array.ConvertAll(files, file => new FileStream(
    file, FileMode.Create, FileAccess.Write, FileShare.ReadWrite));

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
