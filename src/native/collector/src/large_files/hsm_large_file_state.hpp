#pragma once

/// @file
/// @brief The state files of large files (#1887), the managed collector's format (#1888 §4, §12.4):
/// `<data>/large-files/watch-<key>.json` per watch (key: the first 16 hex of SHA-256 of
/// `folder|mask|sensor`, the folder in its key form), `sent-<sha16(sensor)>.json` for handed-over files,
/// and per upload in progress `upload-<sha16(path)>.json` (the upload id, the facts the file was begun
/// with, the counters that end in "given up") with `upload-<sha16(path)>.digests` (32 bytes per piece,
/// indexed by piece number, all zero = absent). Each JSON file is guarded by a lock between processes
/// and replaced whole. Missing: empty, silent. Corrupt: renamed `.corrupt-<UTC>` and started empty, said.
/// Internal; nothing throws but std::bad_alloc.

#include "hsm_large_file_platform.hpp"
#include "hsm_sha256.hpp"

#include <cstdint>
#include <set>
#include <string>
#include <vector>

namespace hsm::collector::large_files
{
    /// What names a source file in the state: a new file under an old name is a new file. The identity
    /// is the persisted form (POSIX: the inode only).
    struct FileKey
    {
        std::string name;
        std::int64_t size = 0;
        std::int64_t modified_ticks = 0;
        std::string identity;

        bool operator==(const FileKey& other) const noexcept
        {
            return name == other.name && size == other.size && modified_ticks == other.modified_ticks && identity == other.identity;
        }
    };

    struct DeliveredEntry
    {
        FileKey key;
        std::int64_t stored_ticks = 0;
    };

    struct GivenUpEntry
    {
        FileKey key;
        std::string reason;
    };

    /// First 16 lower-case hex digits of the SHA-256 of `text` (UTF-8).
    std::string Sha16(const std::string& text);

    /// One watch's (or one sensor's handed-over files') record of delivered and given-up files.
    class FolderState
    {
    public:
        /// `file` is the full path of the JSON file; `folder`, `mask`, `sensor` are written into it.
        FolderState(std::string file, std::string folder, std::string mask, std::string sensor);

        /// Takes the lock and reads the file. Busy: another process holds it. A corrupt file is renamed
        /// and `note` says so (the caller reports it once).
        LockedStateFile::Result Load(std::string& note, std::string& error);
        /// Releases the lock (a watch released, a stop).
        void Unload() noexcept;
        bool Loaded() const noexcept { return lock_.Locked(); }

        const DeliveredEntry* FindDelivered(const FileKey& key) const;
        const GivenUpEntry* FindGivenUp(const FileKey& key) const;
        void SetDelivered(const FileKey& key, std::int64_t stored_ticks);
        void SetGivenUp(const FileKey& key, const std::string& reason);
        void Forget(const FileKey& key);
        /// Drops the entries whose name is not in `present` (the folder's names now); true when any went.
        bool Prune(const std::set<std::string>& present);
        bool Save(std::string& error);

        const std::vector<DeliveredEntry>& Delivered() const noexcept { return delivered_; }
        const std::vector<GivenUpEntry>& GivenUp() const noexcept { return given_up_; }

    private:
        std::string file_;
        std::string folder_;
        std::string mask_;
        std::string sensor_;
        LockedStateFile lock_;
        std::vector<DeliveredEntry> delivered_;
        std::vector<GivenUpEntry> given_up_;
    };

    /// The record of one upload in progress, kept from the first `begin` until `stored` or a discard.
    struct UploadFacts
    {
        std::string path;   // the source file
        std::string sensor; // the File sensor's registered path
        FileKey key;        // the facts it was begun with
        std::int64_t piece_bytes = 0;
        std::string upload; // the server's upload id
        bool secure = true; // part of the upload's key
        int discards = 0;
        int checksum_mismatches = 0;
        int protocol_closes = 0;
    };

    class UploadRecord
    {
    public:
        /// `json_file` is `upload-<sha16(path)>.json`; the digest list sits beside it.
        explicit UploadRecord(std::string json_file);

        LockedStateFile::Result Load(bool& exists, std::string& note, std::string& error);
        bool Save(std::string& error);
        /// Removes the record and its digest list and releases the lock.
        void Remove() noexcept;
        void Unload() noexcept;
        bool Loaded() const noexcept { return lock_.Locked(); }

        UploadFacts& Facts() noexcept { return facts_; }
        const UploadFacts& Facts() const noexcept { return facts_; }

        /// Opens the digest list of `pieces` entries; another length or piece size drops what was there.
        bool OpenDigests(std::int64_t pieces, std::string& error);
        /// The digest of piece `index`, false when absent.
        bool Digest(std::int64_t index, Sha256Digest& out) const;
        bool SetDigest(std::int64_t index, const Sha256Digest& digest, std::string& error);
        std::int64_t Pieces() const noexcept { return static_cast<std::int64_t>(digests_.size() / 32); }

    private:
        std::string file_;
        LockedStateFile lock_;
        UploadFacts facts_;
        RecordFile digest_file_;
        std::vector<std::uint8_t> digests_;
    };

    /// The upload records of a data folder (the names of `upload-*.json`), for the check at Start.
    std::vector<std::string> ListUploadRecords(const std::string& state_folder);
} // namespace hsm::collector::large_files
