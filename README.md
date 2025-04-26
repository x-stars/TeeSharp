# TeeSharp

A **toy** implementation of the `tee` tool in .NET.

Features:

* Asynchronous I/O
* Parallel output
* Multi-platform support
* Native AOT compiling

## Command Line Usage

``` bat
> tee-cs --help
Usage: tee-cs[.exe] [OPTION]... [FILE]...
Copy standard input to each FILE, and also to standard output.

    -a, --append            Append to the given FILEs, do not overwrite.
    -b, --buffer-size N     Buffer size N using in copying, default to 4096.
    -?, -h, --help          Display this help and exit.

If a FILE is -, copy again to standard output.
```

> `tee-fs` also works.
