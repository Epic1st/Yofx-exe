# Broker worker process boundary

The gateway host never loads the MT5 adapter or vendor assembly. Each send or
reconciliation operation uses a new child process with redirected standard input,
output, and error streams. The parent supplies a fresh 256-bit session key over the
private input pipe, and both length-delimited messages use direction-specific
HMAC-SHA256 authentication. JSON parsing rejects unknown or missing constructor
members, and request/response frames, collections, text, durations, and deadlines
have explicit upper bounds.

The configured launch manifest is itself SHA-256 pinned. It must exhaustively list
every file in a dedicated worker deployment directory, including the entrypoint,
managed assemblies, dependency metadata, runtime configuration, and native files.
Every listed digest is verified and every file is held open read-only for the child
lifetime. Deployments should prefer the project's published single-file output.
The executable and manifest must be ordinary files in the same dedicated local
directory on a ready fixed volume. UNC/device paths, network or removable volumes,
and reparse-point ancestry are rejected before deployment files are opened. The
deployment directory must be immutable to the gateway identity; this code does not
attempt to defeat a privileged same-host actor that can rewrite directory entries
or the already-running gateway process. The operating system, local filesystem, and
the .NET runtime already hosting the gateway are part of the trusted computing base.

One absolute deadline begins before request serialization and launch-manifest
verification. The same deadline token covers hashing, launch checks, bootstrap,
request/response IPC, and child exit. The deadline is recomputed immediately before
`Process.Start` and before bootstrap, so verification cannot consume the budget and
give a late child a fresh window. `Process.Start` itself is a synchronous operating
system call and cannot be preempted by this managed boundary. File and directory
metadata reads, opens, and enumeration are also synchronous and cannot be preempted
by the token; restricting launch closure paths to a local fixed volume reduces but
does not remove that stall risk. If `Process.Start` returns a live child after the
deadline, no bootstrap is sent and fail-closed cleanup is requested. The calling
thread can still remain blocked inside a synchronous filesystem or OS call beyond
the deadline. A true wall-clock hard bound requires a separately contained,
killable outer launch/verification helper and remains an activation blocker.

Cancellation, deadline expiry, malformed output, failed authentication, trailing
output, or a non-zero exit all fail closed. A live child receives a best-effort
`Kill(entireProcessTree: true)` request followed by a bounded wait on the root
process handle. .NET does not expose proof that every descendant stopped, so root
exit is not treated as process-tree confirmation: every path that required the tree
kill surfaces only `mt5_process_termination_unconfirmed`. Child output, paths,
command material, and underlying exceptions are never logged.

This is a same-host fail-closed process mechanism with bounded protocol sizes and a
bounded root-handle cleanup wait; it is not a wall-clock-bounded containment
mechanism, sandbox, Windows job-object, container, VM, privilege boundary,
filesystem boundary, or network policy. It does not satisfy or bypass the separate
isolated-runner/vendor-trust gate. Production submission remains disabled in
`BrokerCommandCoordinatorOptions`, and the worker currently composes only
`Mt5ProofOnlyBrokerWorkerExecutor`, which makes no vendor call and cannot submit a
trade.
