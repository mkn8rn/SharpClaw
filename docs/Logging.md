# Logging

SharpClaw writes process logs under the active instance directory in local
application data. A typical Windows development run creates a SharpClaw
folder under `%LOCALAPPDATA%`, with separate instance folders for the
interface, the core backend, and the gateway. Each instance has its own
`logs` directory, and each running process writes into the subdirectory
named after the process, such as `uno`, `core`, or `gateway`.

The session log files are intentionally split by audience. `log.txt`
contains the normal `ILogger` stream for informational, warning, error, and
critical entries, the mirrored Serilog event stream, and anything written to
standard output through `Console.WriteLine`. `debug.txt` contains debug and
trace entries, including `System.Diagnostics.Debug.WriteLine` and
`Trace.WriteLine` output that also appears in Visual Studio's debug output
pane. This is where chat timing, streaming timing, task step timing, and
other diagnostic breadcrumbs appear when the configured minimum level allows
them. `exceptions.txt` contains exception details, standard error output,
and exceptions attached to Serilog events, and is flushed immediately when an
exception is captured. `serilog.txt` contains the raw Serilog file sink. It
is separate from `log.txt` so Serilog and the session writer do not compete
for the same file handle.

The logging settings live in each process environment file under
`Logging:Serilog`. The same shape is used by Core, Gateway, and Interface.
For example, setting `MinimumLevel` to `Debug` enables the structured timing
logs added around chat requests, streaming chat, native tool-loop rounds,
module tool execution, and task steps. `FileEnabled` controls whether
Serilog writes `serilog.txt`; the session logger still writes `log.txt`,
`debug.txt`, and `exceptions.txt` whenever it is registered by the process.
`RequestLoggingEnabled` controls ASP.NET Core request logging in Core and
Gateway and is not used by the Uno interface. No extra environment variables
are required for console, diagnostics, or exception capture; those bridges
are installed by the process when the session logger is created.

For day-to-day troubleshooting, start with `exceptions.txt` if something
failed visibly, then read `log.txt` for the surrounding service events. If a
chat or task feels slow, temporarily set `Logging:Serilog:MinimumLevel` to
`Debug` in the relevant process env file and restart that process. The
debug file will then show request IDs, elapsed milliseconds for history
loading, provider calls, streamed provider rounds, assistant message
persistence, cost aggregation, task compilation, and task step execution.
When the investigation is done, return the minimum level to `Information`
unless the additional diagnostic volume is still useful.

If a session log file is empty, first confirm that the process is the one
you are exercising. The desktop interface can launch a bundled backend and
gateway, each with its own instance log directory, while a manually run API
or gateway process may use a different instance root. Also check the
minimum level. `debug.txt` is expected to stay quiet at `Information` unless
code writes directly to the debug or trace stream, while `log.txt` should
receive normal `ILogger`, Serilog, and stdout output from Core, Gateway, and
Interface after startup.
