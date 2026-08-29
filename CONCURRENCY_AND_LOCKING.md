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
