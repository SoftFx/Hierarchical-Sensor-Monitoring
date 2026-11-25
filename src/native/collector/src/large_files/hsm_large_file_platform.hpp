#pragma once

/// @file
/// @brief What large files need from the operating system (#1887): the top level of a folder, a source
/// file kept open for its whole upload (Windows: writers and deleters denied), its facts and identity,
/// reads at an offset, a delete of exactly the file that was sent, locked state files written whole, the
/// default data folder. Windows (Win32, UTF-16 inside, UTF-8 at this interface) and POSIX. Internal.
///
/// Times are 100 ns ticks since 1970-01-01 UTC everywhere, so a live time and a persisted one compare
/// at the precision the wire writes. Sizes and offsets are 64-bit. Nothing here throws except
/// std::bad_alloc from a string or a vector.

#include <cstddef>
#include <cstdint>
#include <functional>
#include <string>
#include <vector>

namespace hsm::collector::large_files
{
    constexpr std::int64_t kTicksPerSecond = 10000000;

    /// One entry of a folder's top level: a regular file, or a link / reparse point (flagged, never
    /// taken). Directories are left out.
    struct FolderEntry
    {
        std::string name;     // UTF-8; empty when name_valid is false
        std::string raw_name; // the bytes as the system gave them (POSIX), for the report of a bad name
        std::int64_t size = 0;
        std::int64_t modified_ticks = 0;
        bool is_link = false;
        bool name_valid = true;
    };

    enum class ListResult
    {
        Ok,
        Missing,
        Failed,
    };

    /// The top level of `folder` (absolute, UTF-8).
    ListResult ListFolder(const std::string& folder, std::vector<FolderEntry>& out, std::string& error);

    /// What the collector compares before `end` and before a delete.
    struct SourceFacts
    {
        std::int64_t size = 0;
        std::int64_t modified_ticks = 0;
        /// The wire `source`: Windows `vol:<16 hex>/file:<32 hex>`, POSIX `ino:<hex>`; empty when the
        /// system gives none (a zero file id).
        std::string identity;
        /// POSIX: the device, compared within a run only (it is not stable across reboots). 0 on Windows.
        std::uint64_t device = 0;

        bool SameAs(const SourceFacts& other) const noexcept
        {
            return size == other.size && modified_ticks == other.modified_ticks && identity == other.identity && device == other.device;
        }
    };

    enum class OpenResult
    {
        Ok,
        Missing,
        /// Windows: a sharing violation (a writer, or a holder that does not share reading or deleting).
        Busy,
        /// A link or a reparse point: never taken.
        Link,
        Denied,
        Failed,
    };

    enum class DeleteResult
    {
        Deleted,
        /// The file at the path is no longer the one that was sent; nothing deleted.
        Changed,
        Failed,
    };

    /// A source file open for an upload. Windows: read and DELETE access, sharing read only, opened on
    /// the reparse point itself (a link is refused, never followed). POSIX: O_RDONLY | O_NOFOLLOW.
    class SourceFile
    {
    public:
        SourceFile() = default;
        ~SourceFile();

        SourceFile(const SourceFile&) = delete;
        SourceFile& operator=(const SourceFile&) = delete;

        OpenResult Open(const std::string& path, std::string& error);
        bool IsOpen() const noexcept;
        /// Size, time and identity read from the open handle.
        bool Facts(SourceFacts& out, std::string& error) const;
        /// Reads exactly `size` bytes at `offset` in slices of 1 MiB, asking `stop` between slices. False
        /// with `error` on a failure, a short read (the file shrank) or a stop.
        bool ReadAt(std::int64_t offset, std::uint8_t* buffer, std::size_t size, const std::function<bool()>& stop, std::string& error);
        /// Deletes exactly this file: Windows through the open handle (FileDispositionInfo); POSIX after
        /// stat(path) names the same device and inode as the open descriptor, which stays open over the
        /// unlink (a replacing file cannot take the inode meanwhile). The handle is closed afterwards.
        DeleteResult DeleteThis(const std::string& path, std::string& error);
        void Close() noexcept;

    private:
#if defined(_WIN32)
        void* handle_ = nullptr; // HANDLE; INVALID_HANDLE_VALUE is kept as nullptr
#else
        int fd_ = -1;
#endif
    };

    /// Facts of the file at `path` without opening it for an upload (no link followed); false when it is
    /// not there, is a link or cannot be read.
    bool PathFacts(const std::string& path, SourceFacts& out, std::string& error);

    /// A state file guarded by an exclusive lock on `<path>.lock`, held while the object lives: the lock
    /// is between processes (Windows: the lock file open with no sharing; POSIX: flock), the collector's
    /// own threads are serialized by its own locks. Writes replace the file whole (a unique temporary
    /// name, flushed, then renamed over it), so a kill leaves the old or the new content.
    class LockedStateFile
    {
    public:
        enum class Result
        {
            Ok,
            Busy,
            Failed,
        };

        LockedStateFile() = default;
        ~LockedStateFile();

        LockedStateFile(const LockedStateFile&) = delete;
        LockedStateFile& operator=(const LockedStateFile&) = delete;

        Result Lock(const std::string& path, std::string& error);
        bool Locked() const noexcept;
        const std::string& Path() const noexcept { return path_; }
        /// False when the file is not there (`missing` true) or cannot be read.
        bool Read(std::string& content, bool& missing, std::string& error) const;
        bool Replace(const std::string& content, std::string& error);
        /// Removes the file (not the lock); true when it is gone.
        bool Remove(std::string& error);
        void Unlock() noexcept;

    private:
        std::string path_;
#if defined(_WIN32)
        void* lock_ = nullptr;
#else
        int lock_ = -1;
#endif
    };

    /// A fixed-size file of `count` records of `record_bytes`, written in place (the digest list).
    class RecordFile
    {
    public:
        RecordFile() = default;
        ~RecordFile();

        RecordFile(const RecordFile&) = delete;
        RecordFile& operator=(const RecordFile&) = delete;

        bool Open(const std::string& path, std::int64_t count, std::size_t record_bytes, std::string& error);
        bool IsOpen() const noexcept;
        bool ReadAll(std::vector<std::uint8_t>& out, std::string& error) const;
        bool Write(std::int64_t index, const std::uint8_t* record, std::string& error);
        void Close() noexcept;

    private:
        std::int64_t count_ = 0;
        std::size_t record_bytes_ = 0;
#if defined(_WIN32)
        void* handle_ = nullptr;
#else
        int fd_ = -1;
#endif
    };

    bool RemoveFile(const std::string& path, std::string& error);
    bool RenameFile(const std::string& from, const std::string& to, std::string& error);
    bool MakeFolders(const std::string& path, std::string& error);
    /// Absolute form of `path` from the current directory now; empty when it cannot be made.
    std::string AbsolutePath(const std::string& path);
    std::string JoinPath(const std::string& folder, const std::string& name);
    std::string FileNameOf(const std::string& path);
    std::string FolderOf(const std::string& path);
    /// The base of the default data folder: Windows FOLDERID_LocalAppData; elsewhere $XDG_DATA_HOME,
    /// else $HOME/.local/share. Empty when there is none (a service without a profile): the caller falls
    /// back to TempFolder and says so.
    std::string LocalDataBase();
    std::string TempFolder();
    /// The executable of this process (UTF-8); empty when unknown.
    std::string ProcessPath();
    /// Windows paths compare without case: the key of a folder is lower-cased there (ASCII and the
    /// system's own rules for the rest); elsewhere the path as it is.
    std::string PathKeyForm(const std::string& path);
    bool IsValidUtf8(const std::string& text) noexcept;
    std::int64_t ProcessId() noexcept;

    /// The uploader thread's priority: lowered at its start, normal again when a stop is asked.
    void LowerCurrentThreadPriority(std::string& error);
    /// A token for the current thread that RaiseThreadPriority and CancelThreadIo take from another
    /// thread (Windows: a duplicated thread handle; POSIX: the thread id).
    std::uintptr_t CurrentThreadToken();
    void ReleaseThreadToken(std::uintptr_t token) noexcept;
    void RaiseThreadPriority(std::uintptr_t token) noexcept;
    /// Windows: cancels a synchronous read or flush the thread is blocked in (CancelSynchronousIo).
    /// POSIX: nothing (a read blocked on a dead network mount holds the stop; documented).
    void CancelThreadIo(std::uintptr_t token) noexcept;
} // namespace hsm::collector::large_files
