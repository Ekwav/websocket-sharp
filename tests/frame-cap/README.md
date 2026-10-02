Run from the repository root with .NET 10:

```sh
./tests/frame-cap/run.sh
```

The console regression compiles the library sources directly without package
references. It opens only IPv4 loopback sockets on ephemeral ports. It checks
both client and server receiving 8,201 numbered messages in order, using paced
and burst sends, then checks ping and a clean close without callback errors.

The old receiver stops after 8,001 delivered messages. Its cumulative counter
counts each successfully processed wire frame, including fragment and control
frames, rather than only complete application messages. On client sockets its
logging also dereferences a null server context. Burst traffic can cause a
separate stack overflow because BeginRead callbacks may complete inline.

The fix queues the next frame read, keeping one outstanding read and preserving
the existing FIFO message queue and its lock that serializes event dispatch.
The queue boundary breaks recursive inline callbacks. A received close frame
still follows processCloseFrame and signals receivingExited through the existing
stop path. Closing concurrently with a queued read retains the existing reader
error/abort path; no new connection state or shutdown mechanism is introduced.

These regressions establish library defects; they do not establish the cause
of a particular production disconnect interval.
