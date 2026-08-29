# Concurrency and Locking in S3Drive

S3Drive sits between two worlds that disagree about concurrency. On the front, Windows and Dokan
issue many filesystem calls at once, from multiple threads and multiple processes, and expect a
normal read/write drive. On the back, an S3 object is an immutable, all-or-nothing blob: there is
no partial write, no append, and no in-place edit. Reconciling the two safely is the entire job of
this layer.

The guiding rule is deliberate and non-negotiable: **data consistency and coherency come first,
even at the cost of concurrency.** When two operations could interfere in a way that would corrupt
the backing S3 object, S3Drive serializes them rather than letting them race. This document
describes exactly how that is done and where the guarantees begin and end.

## The two levels of concurrency

There are two independent axes to control.

**Cross-process.** More than one program could try to drive the same bucket: two agents, or an
agent plus a stray copy. S3Drive prevents this structurally with a single-instance guard. The
tray agent is the only process that ever holds a Dokan mount, and only one agent can run at a
time. Everything below the agent is therefore single-process by construction.

**Cross-thread.** Within that one agent process, Dokan is multi-threaded — the mount is built
without `DokanOptions.SingleThread`, so several Dokan worker threads call into the filesystem
concurrently, sometimes against the same path. This is where the per-object locking lives.

Because there is exactly one process and its threads are coordinated, the backing S3 data cannot
be corrupted by S3Drive's own concurrent access.

## Single-instance enforcement (cross-process)

`S3Drive.Core.Concurrency.AgentInstanceLock` (`src/S3Drive.Core/Concurrency/AgentInstanceLock.cs`)
acquires an exclusive OS file lock on `~/.s3drive/state/agent.lock`:

```csharp
FileStream stream = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
```

`FileShare.None` means the operating system refuses a second open of that file from any process. The
agent acquires the handle at startup and holds it for its whole lifetime (`FileLockHandle`,
disposed only on exit). A second agent's `TryAcquire` returns `null`, and that process exits with
code 0 rather than mounting anything. `AgentInstanceLock.IsRunning` performs the same probe
non-destructively, and the TUI uses it to decide whether it needs to launch the agent.

The consequence that matters for correctness: since only the single agent process mounts drives,
no two S3Drive processes can ever issue conflicting writes to the same object. The cross-process
problem is removed rather than mediated.

### File-based coordination between the TUI and the agent

The TUI and the agent still share three things on disk, and each is written so a concurrent reader
never sees a torn file:

- **Configuration** (`s3drive.json`), via `SettingsManager`.
- **Status** (`state/status.json`), via `StatusStore`.
- **Commands** (`state/commands/cmd-*.json`), via `CommandChannel`.

All three write **atomically** — content is written to a temporary sibling file and then moved into
place with `File.Move(temp, final, overwrite: true)`, so a reader either sees the whole previous
file or the whole new one, never a partial write. Reads that can race with a replace open the file
with `FileShare.ReadWrite | FileShare.Delete` and retry briefly on `IOException`. Command files are
uniquely named per command, so the agent drains and deletes them without ever colliding with the
TUI that is still writing the next one.

## Per-object locking (cross-thread)

Inside the agent, the unit of mutual exclusion is a single S3 object key.

`S3Drive.Core.Concurrency.ObjectLocks` (`src/S3Drive.Core/Concurrency/ObjectLocks.cs`) wraps
[Padlock](https://github.com/jchristn/padlock) as `Padlock<string>(maxCount: 1, poolSize: 64)`.
`maxCount: 1` makes each named lock a mutex: only one holder per key at a time. Acquisition returns
a disposable handle, so the critical section is a `using` block.

Each mounted drive owns its own `ObjectLocks` instance (created in
`MountManager.MountAsync`). Keys are therefore naturally scoped per drive — the same key in two
different buckets never contends.

### What the lock actually protects — and why it is narrow

The object lock is **not** held for the whole lifetime of an open file handle. It is acquired only
around the specific sections that touch S3, and released immediately after. From
`S3Drive.Core.FileSystem.S3DriveFileSystem`:

- **Staging a read/modify** — the first time a handle needs the object's current bytes, the whole
  object is downloaded to a local staging file under the lock:

  ```csharp
  using (_Locks.Acquire(context.Key)) { RunGetToFile(context.Key, path); }
  ```

- **Persisting a write** — on `Cleanup` (handle close), a dirty staging file is written back as a
  whole object under the lock:

  ```csharp
  using (_Locks.Acquire(context.Key)) { RunPutFromFile(context.Key, context.StagingPath); }
  ```

- **Deleting** — a pending delete removes the object (or directory marker) under the lock.

- **Renaming a file** — `MoveFile` copies then deletes the source under the source key's lock.

Holding the lock only for these windows is a deliberate choice, not an oversight. If the lock were
held for an entire open→close handle lifetime, it would **deadlock**: Windows and Dokan routinely
issue a second `CreateFile` on the same path (to read attributes, for example) while a handle is
already open, and with a per-key mutex the second open would block on the first forever. Scoping the
lock to the S3-touching critical sections keeps the backing writes serialized while leaving the
handle lifecycle free of cross-open deadlocks.

A second, finer lock guards the per-handle staging state. Each open handle carries a
`FileContext` with its own monitor (`context.Sync`); `ReadFile`, `WriteFile`, and the truncation
paths take it so that concurrent Dokan calls on the *same* handle serialize their access to that
handle's staging file and dirty flag.

### Why there are no byte-range locks

Windows offers `LockFile`/`UnlockFile` for byte-range locking. S3Drive implements both as no-ops
that return success. A byte range has no meaning against a whole-object backing store: you cannot
lock or modify part of an S3 object, so a range lock could not be honored even in principle. All
coherency is enforced instead by the coarse per-object lock described above. This is exactly the
trade the project chose — correctness of the whole object over fine-grained partial concurrency.

## Other synchronized state

Two more components are internally thread-safe because they are touched from multiple Dokan threads:

- **`MetadataCache`** (`src/S3Drive.Core/Storage/MetadataCache.cs`) guards its listing and
  attribute dictionaries with a single lock. It caches directory listings and object HEADs for a
  configurable time-to-live and invalidates the affected entries immediately on any local mutation,
  so the drive always reflects its own writes without waiting for the TTL.
- **`MountManager`** (`src/S3Drive.Core/Mounting/MountManager.cs`) guards its set of active mounts
  with a lock, so mount, unmount, and status snapshots are consistent even when the tray and the
  command loop act at the same time.

## The write model this locking supports

Understanding the locks requires understanding the write path they protect. Because S3 objects are
immutable and support no partial writes:

1. Opening a file for write stages a local temporary file. For a brand-new file the stage starts
   empty; for an existing file the current object is first downloaded into the stage (a
   read-modify-write).
2. Every `WriteFile`, `SetEndOfFile`, and `SetAllocationSize` edits the local stage, never S3.
3. On `Cleanup` (close), if the stage is dirty, the whole stage is `PutObject`'d as the object —
   one atomic, all-or-nothing S3 write, taken under the object lock.

Serializing step 3 per key is what guarantees coherency: two writers to the same key cannot
interleave their PUTs, so the object is never left in a spliced or half-written state.

## Two consumers accessing the same files or folders

"Two consumers" can mean two threads in one application, or two entirely separate applications
opening files on the mounted drive. The distinction does not change the behavior: every open, read,
write, and delete from any consumer travels through Dokan into the **one** agent process, so all of
them are mediated by the same per-object locks and per-handle monitors described above. There is no
second process in the picture to coordinate with (see single-instance enforcement).

Two properties shape every scenario below:

- **Windows share modes are not enforced.** `CreateFile` receives the requested `FileShare` but
  ignores it. S3Drive never returns a sharing violation; a second open of a file already open — even
  one another consumer requested exclusively — always succeeds. Coherency is provided by the
  per-object lock and the whole-object write model, not by denying access.
- **Each open handle is independent.** Every `CreateFile` produces its own `FileContext` with its
  own local staging file. Two consumers with the file "open at the same time" are really working
  against two separate local snapshots; S3 is touched only at defined moments (stage-in on first
  read, `PutObject`/`DeleteObject` on close), each taken under the object lock.

| Scenario | What happens | Outcome |
|---|---|---|
| Two consumers **read** the same file | Each stages its own copy of the object (the downloads serialize under the key lock; reads then come from each consumer's local stage) | Both succeed; both see a consistent point-in-time snapshot |
| One **reads** while another **writes** the same file | The reader serves bytes from the snapshot it staged at open; the writer edits its own stage and PUTs on close under the lock | The reader is unaffected by the in-flight write; it sees the new bytes only on a subsequent open |
| Two consumers **write** the same file | Each edits its own stage; on close each PUTs the whole object, and the PUTs serialize per key | **Last close wins**, whole-object — no merge, no partial blend, and no error raised |
| Multiple threads use the **same handle** | `ReadFile`/`WriteFile`/truncation take that handle's `context.Sync` monitor | Access to that one stage and its dirty flag is serialized |
| Two consumers **create the same new file** (`CreateNew`) | The existence probe (`HeadObject`) and the final PUT are **not** a single atomic step | Under a tight race both can pass the probe; the later close wins and `ObjectNameCollision` is not guaranteed |
| One **writes** while another **deletes** the same file | Both the PUT and the delete run under the key lock, ordered by whichever handle's `Cleanup` runs last | If the write closes last it **resurrects** the object; if the delete runs last the object is gone — no error either way |
| **Rename** a file vs. read/write of it | `MoveFile` copies then deletes the source under the source key's lock; an open reader keeps serving its own stage | Safe per object, but a write that closes after the rename can leave bytes under the old key (last-write-wins per key) |
| Two consumers touch **different files in the same folder** | Different keys use different locks, so there is no mutual exclusion between them; each mutation invalidates the shared parent-listing cache entry | Fully concurrent; listings stay correct via invalidation |
| **Delete or rename a folder** while a file under it is open | The bulk operation processes the set of objects it enumerated, one at a time, while the open handle works from its own stage | Non-atomic: an interleaved write can recreate an object the bulk delete already removed, or land under the old prefix during a rename |

The through-line is that S3Drive never corrupts an individual object no matter how consumers
interleave, but it also never merges concurrent changes and never blocks one consumer to protect
another's uncommitted work. Where two consumers genuinely conflict, the result is deterministic
only in that the **last close wins**; it is not a transaction across multiple objects.

## Parallelism across different objects

Operations on **different** object keys are not serialized against each other — S3Drive is built to
let them run concurrently:

- **The mount is multi-threaded.** It is created with `DokanOptions.MountManager |
  DokanOptions.EnableNetworkUnmount` and *without* `DokanOptions.SingleThread`
  (`MountManager.cs`), so Dokan dispatches filesystem callbacks on several worker threads at once.
- **The per-object lock is genuinely per-key.** `Padlock<string>(1, 64)` tracks each distinct key
  independently; the `64` is a pool size for *reusing* lock entries, not a fixed set of stripes, so
  two different keys never falsely block one another. Operations on separate files stage, PUT, and
  delete in parallel.
- **The metadata cache and mount registry** use short internal locks only, so they are not a
  serialization point for object I/O.

So the ceiling on parallelism inside S3Drive is set by how many Dokan worker threads are active and
how many concurrent connections the underlying AWS SDK client will open — not by any global lock.
Each worker thread does block synchronously on its own S3 call (the filesystem layer bridges
Dokan's synchronous callbacks to async storage calls with `GetAwaiter().GetResult()`), but other
threads keep working meanwhile.

**In practice the *consumer* decides the actual degree of parallelism**, which is the important
caveat for something like a drag-and-drop:

- **Windows Explorer** copies a folder largely **sequentially** — it opens, writes, and closes one
  file before moving to the next, with only limited pipelining. Dropping a directory onto the drive
  therefore tends to upload one file at a time even though S3Drive would allow more. Each file is
  still a full open → stage/write → `PutObject`-on-close cycle.
- **Tools that issue parallel I/O** — `robocopy /MT`, parallel PowerShell, build tools, backup
  software — *do* get real concurrency, because S3Drive imposes no cross-key serialization. Several
  files upload or download at the same time, one per in-flight handle.

Two cases where parallelism is bounded regardless of the consumer:

- **The same object.** Concurrent operations on one key serialize on that key's lock (see the
  scenarios above). This is by design.
- **S3Drive's own directory rename/move.** The internal `MoveDirectory` copies each descendant
  object **sequentially** (a `GetObject`+`PutObject` per key in a loop) and then removes the old
  keys in a **single batched** multi-object delete. The copy phase is not parallelized today; it is
  the most obvious place to add bounded parallelism later. (A drag-and-drop *copy* from outside is
  driven by the consumer, not by `MoveDirectory`.)
- **A single large file.** One object is one PUT; the AWS SDK may internally split it into parallel
  multipart uploads, but that concurrency is within one file and is delegated to the SDK.

## Guarantees and their boundaries

**What S3Drive guarantees.** Within S3Drive itself, the backing object for a given key is never
corrupted by concurrent access. Writes to a key are serialized and atomic, one process owns every
mount, and S3's own strong read-after-write consistency means a completed PUT is immediately
visible to the next read.

**Where the guarantee stops — be explicit about this:**

- **Last-write-wins, no merge.** Two S3Drive writers that both open the same file, edit their own
  stages, and close will each PUT the whole object. The locks serialize the PUTs so the object is
  always intact, but the second PUT overwrites the first — there is no field-level or byte-level
  merge, because object storage has none. This is inherent to mapping a mutable file onto an
  immutable object.
- **No cross-writer conflict detection.** S3Drive does not use conditional writes (S3
  `If-Match`/ETag preconditions). It does not detect that an object changed between the stage
  download and the final PUT; the last close wins.
- **External writers are outside the lock.** The per-object lock only coordinates S3Drive's own
  threads. If another S3 client writes to the same bucket while a drive is mounted, S3Drive cannot
  serialize against it. S3's strong consistency means a subsequent read returns the other client's
  latest bytes, but S3Drive's metadata cache may show stale listings or sizes until its TTL expires
  (set `MetadataCacheSeconds` lower, or to 0 to disable caching, if you expect concurrent external
  writers).
- **Directory rename/delete is a bulk, non-atomic operation.** Renaming or recursively removing a
  "folder" copies or deletes many objects one at a time. Each object operation is safe, but the
  batch as a whole is not a single atomic transaction — an interruption can leave a partially
  moved tree, exactly as it would with any object-store client.

These boundaries are the honest cost of the design. Within them, the priority stated at the top
holds: consistency and coherency first.
