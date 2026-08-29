# S3 Operations in S3Drive

S3Drive presents a bucket as a read/write Windows drive, but the operations Windows expects and the
operations S3 provides do not line up one to one. A file is mutable and seekable; an S3 object is an
immutable, all-or-nothing blob addressed by a flat key. A directory is a real container; in S3 there
are only key prefixes. This document explains, operation by operation, how each filesystem call is
translated into S3 requests, and — just as importantly — where the object-storage model imposes hard
limits.

Storage access goes through [Blobject](https://github.com/jchristn/blobject) (`Blobject.AmazonS3`),
which wraps the AWS SDK. The relevant code is `S3Drive.Core.Storage.BlobS3Store`
(`src/S3Drive.Core/Storage/BlobS3Store.cs`) behind the `IS3Store` interface, and
`S3Drive.Core.FileSystem.S3DriveFileSystem` (`src/S3Drive.Core/FileSystem/S3DriveFileSystem.cs`),
which implements Dokan's `IDokanOperations`.

## The core mapping

| Filesystem operation (Dokan) | S3Drive method | Blobject call | Underlying S3 request |
|---|---|---|---|
| Enumerate a directory (`FindFiles`) | `IS3Store.ListAsync(prefix)` | `EnumerateAsync(EnumerationFilter{Prefix})` | `ListObjectsV2` (by prefix) |
| Recursive enumerate (rename/delete) | `IS3Store.ListAllKeysAsync(prefix)` | `EnumerateAsync(EnumerationFilter{Prefix})` | `ListObjectsV2` (by prefix) |
| Attributes (`GetFileInformation`) | `IS3Store.HeadAsync(key)` | `GetMetadataAsync(key)` | `HeadObject` |
| Existence check | `IS3Store.ExistsAsync(key)` | `ExistsAsync(key)` | `HeadObject` / list |
| Read (`ReadFile`) | stage via `GetToFileAsync`, then read locally | `GetAsync(key)` | `GetObject` (whole object) |
| Write (`WriteFile`) → persist on close | stage locally, then `PutFromFileAsync` | `WriteAsync(key, type, length, stream)` | `PutObject` (multipart for large) |
| Create new object | `PutAsync` / staged then `PutFromFileAsync` | `WriteAsync(...)` | `PutObject` |
| Delete (`DeleteFile`/`DeleteDirectory`) | `IS3Store.DeleteAsync(key)` | `DeleteAsync(key)` | `DeleteObject` |
| Rename/move (`MoveFile`) | `CopyAsync` then `DeleteAsync` | `GetAsync` + `WriteAsync`, then `DeleteAsync` | `GetObject` + `PutObject` + `DeleteObject` |
| Create directory (`CreateDirectory`) | write a `prefix/` marker | `WriteAsync(prefix + "/", type, empty)` | `PutObject` (zero bytes) |
| Connectivity check | `ValidateConnectivityAsync` | `ValidateConnectivity()` | provider probe |

Everything below expands on these rows and the limits behind them.

## Addressing: AWS and S3-compatible endpoints

`BlobS3Store` builds a Blobject `AwsSettings` from the drive profile. For **AWS S3** it uses the
four-argument form (access key, secret key, region, bucket). For an **S3-compatible endpoint**
(Less3, Ceph, MinIO, and others) it passes the endpoint URL and SSL flag, and constructs a
`BaseUrl` template that encodes the addressing style:

- **Path-style** → `{scheme}://{host}/{bucket}/{key}`
- **Virtual-hosted** → `{scheme}://{bucket}.{host}/{key}`

The scheme follows the profile's SSL toggle, and the region defaults to `us-east-1` when unset. This
is why the profile carries `ServiceUrl`, `UseSsl`, and `UsePathStyle` — S3-compatible servers vary in
which addressing they accept, and Blobject has no boolean for it, so the choice is expressed through
the URL template.

## Enumeration and directory traversal

S3 has no directories. A "folder" is just a shared key prefix, and traversal is prefix matching.

**Listing a directory.** `FindFiles` maps the Windows path to a prefix (backslashes become forward
slashes; the drive root is the empty prefix) and calls `ListAsync(prefix)`. Blobject's
`EnumerationFilter` exposes a prefix but **no delimiter**, so the enumeration returns *every* key
under the prefix, recursively. `BlobS3Store.ListAsync` then reproduces delimiter semantics in code:
for each returned key it strips the prefix and looks at the remainder — if the remainder contains a
`/`, the first segment is a subfolder; otherwise it is a file. Distinct subfolder names and files
are folded into the set of immediate children that Windows sees.

**Empty folders.** Because a prefix only exists if some object carries it, an empty folder would
otherwise vanish. `CreateDirectory` therefore writes a zero-byte marker object at `prefix/`, and
`DirectoryExists` treats a directory as present when either the marker exists or any child is
returned by a listing. Listings hide the marker itself and synthesize the folder entry from the
prefix.

**Traversal.** There is no tree to walk. Each directory query is an independent prefix listing.
Results are cached briefly by `MetadataCache` (keyed by prefix) so repeated Explorer refreshes of the
same folder do not re-list the bucket every time.

## Attributes and existence

`GetFileInformation` and existence checks use `HeadObject` via `HeadAsync`/`ExistsAsync`. A HEAD
returns the object's size, last-modified time, and ETag, which become the file's length and
timestamps. Windows-specific attributes have no S3 equivalent, so files report a plain "normal"
attribute and folders report "directory." When a file is open for write and has local staged
content, `GetFileInformation` reports the **staging file's** current length rather than the last
HEAD, so a program that writes and then stats the same handle sees the new size.

## Reads

S3 objects are retrieved whole. Blobject's `GetAsync` returns the entire object; there is no ranged
read. To serve Windows' arbitrary offset/length `ReadFile` calls, S3Drive **stages the whole object
to a local temporary file** on first access (`GetToFileAsync` → `GetObject`), then satisfies every
`ReadFile` from that local file with a seek and read. The staging file lives under
`~/.s3drive/cache/<driveId>/` and is removed when the handle closes.

The upside is that random reads are fast and correct once staged. The cost is that opening a large
file for reading downloads all of it first — the first read pays full download latency, and the
object's full size is briefly consumed on local disk.

## Writes, updates, and truncation

S3 objects are immutable: no append, no partial overwrite, no in-place edit. S3Drive maps mutable
file writes onto whole-object replacement using a local stage:

- **New file.** Opening with a create disposition makes an empty staging file. `WriteFile` calls
  fill it. On close, the whole stage is uploaded as the object.
- **Update (modify an existing file).** Opening an existing file for write first downloads the
  current object into the stage (`GetObject`), so the file starts with its real contents. Writes
  edit the stage. On close, the whole stage is uploaded, replacing the object. Every update is thus
  a read-modify-write of the entire object.
- **Truncation.** `SetEndOfFile` and `SetAllocationSize` set the length of the staging file; the
  change is persisted with the whole-object upload on close.
- **Flush.** `FlushFileBuffers` is a no-op — the authoritative write happens once, on close, so that
  a file is uploaded as a single consistent object rather than repeatedly during editing.

The upload itself is `WriteAsync(key, contentType, contentLength, stream)`, streamed from the staging
file so a large object is not held entirely in memory. Multipart upload for large objects is handled
by the underlying AWS SDK inside Blobject.

> Note: the configuration carries a `MultipartThresholdBytes` value, but the actual multipart
> decision is currently delegated to the Blobject/AWS SDK client, which applies its own thresholds.
> Treat the setting as reserved/advisory rather than a hard switch.

## Create and delete

Creating a file is the write path above; a brand-new file with no writes still produces a zero-byte
object on close. Creating a directory writes the `prefix/` marker.

Deletion follows Dokan's two-step model. `DeleteFile`/`DeleteDirectory` only *validate* that the
delete is allowed (the file exists; the directory is empty) and mark the handle; the actual
`DeleteObject` happens in `Cleanup` when the handle closes with deletion pending. `DeleteAsync`
checks existence first and then issues `DeleteObject`, so deleting a missing object is harmless.
Directory deletion refuses a non-empty directory (`DirectoryNotEmpty`) by inspecting a listing.

## Rename and move

S3 has no rename or move. `MoveFile` implements it as copy-then-delete:

- **A file** is copied to the new key and the old key deleted. Blobject has no server-side copy, so
  `CopyAsync` is itself a `GetObject` followed by a `PutObject` — the object is downloaded and
  re-uploaded under the new key, then the old one is deleted.
- **A directory** is renamed by enumerating every descendant key (`ListAllKeysAsync`), copying each
  to the corresponding key under the new prefix, and then deleting each old key.

Both are correct but not free, and this is the sharpest limitation of the model: renaming a large
file downloads and re-uploads all of its bytes, and renaming or moving a large tree is O(number of
objects) round trips. Neither is atomic — an interruption can leave some objects moved and others
not.

## Volume, security, and no-op operations

Some operations have no meaningful S3 backing and are answered synthetically or ignored:

- `GetDiskFreeSpace` reports large fixed capacity and free space — S3 has no per-bucket quota to
  report.
- `GetVolumeInformation` reports a case-preserving, Unicode volume named after the drive.
- `SetFileAttributes` and `SetFileTime` succeed but do nothing — S3 objects carry no Windows
  attributes, and timestamps come from the object's own last-modified time.
- `GetFileSecurity`/`SetFileSecurity` and `FindStreams` return "not implemented"; there are no ACLs
  or alternate data streams on an object.
- `LockFile`/`UnlockFile` (byte-range locks) succeed as no-ops; see
  [`CONCURRENCY_AND_LOCKING.md`](CONCURRENCY_AND_LOCKING.md) for why range locks cannot apply to a
  whole-object store.

## Consistency

S3 provides strong read-after-write consistency: once a `PutObject` or `DeleteObject` completes, the
next `GetObject`/`HeadObject`/`ListObjectsV2` reflects it. S3Drive relies on this — because a write
is a single atomic whole-object PUT and reads see the latest object immediately, a file closed by
one handle is fully visible to the next open. The only staleness comes from S3Drive's own metadata
cache (see limitations), not from S3 itself.

## Limitations, collected

- **Whole-object reads.** No ranged `GetObject` through Blobject, so opening a file downloads the
  entire object to a local stage before any read is served. First-read latency and local disk use
  scale with object size.
- **Whole-object writes.** No append or partial write; every modification is a read-modify-write
  that re-uploads the entire object on close. There are no incremental saves.
- **Rename/move is copy + delete.** No server-side copy, so a rename re-uploads the whole file, and a
  directory rename/move is one copy+delete per descendant object — non-atomic and O(n).
- **Listing is recursive plus client-side folding.** Blobject exposes no delimiter, so a folder
  listing enumerates all keys under the prefix and reconstructs immediate children in code. Deep or
  very large prefixes are proportionally expensive, and there is no exposed control over server-side
  pagination.
- **Empty directories need markers.** Empty folders exist only because S3Drive writes a `prefix/`
  marker object. Folders created by other S3 tools without such markers will not appear as empty
  directories — only prefixes that contain objects show up.
- **Last-write-wins, no conditional writes.** S3Drive does not use ETag/`If-Match` preconditions.
  Concurrent or external overwrites are not detected; the most recent close wins. (Serialization of
  S3Drive's *own* concurrent writers is covered in `CONCURRENCY_AND_LOCKING.md`.)
- **Limited metadata.** Only size and last-modified are represented. Windows attributes, creation
  time as distinct from modification time, ACLs, and alternate streams are not persisted.
- **Metadata-cache staleness.** Directory listings and HEADs are cached for `MetadataCacheSeconds`
  and invalidated on local changes only. An external writer's changes may not appear until the TTL
  expires; lower the value (or set it to 0) when concurrent external writers are expected.
- **Key case sensitivity.** S3 keys are case-sensitive; the Windows filesystem view is
  case-insensitive. Two objects whose keys differ only in case cannot be distinguished through the
  mounted drive.
- **Multipart threshold is delegated.** `MultipartThresholdBytes` is configured but not currently
  wired into Blobject; the underlying SDK decides when to use multipart.
