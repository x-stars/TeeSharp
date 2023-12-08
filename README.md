# TeeSharp

A **toy** implementation of the `tee` tool by .NET.

Features:

* Asynchronous I/O
* Concurrent output
* Multi-platform support
* Native AOT compiling

## Command Line Usage

``` Batch
> TeeSharp.exe --help
Usage: TeeSharp[.exe] [OPTION]... [FILE]...
Copy standard input to each FILE, and also to standard output.

    -a, --append            Append to the given FILEs, do not overwrite.
    -b, --buffer-size N     Buffer size N using in copying.
    -?, -h, --help          Display this help and exit.

When FILE is -, copy again to standard output.
```
