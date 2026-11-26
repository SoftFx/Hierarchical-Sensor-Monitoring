#include "hsm_large_file_state.hpp"

#include "hsm_file_connection.hpp" // FileConnection::FormatTicks

#include <nlohmann/json.hpp>

#include <algorithm>
#include <chrono>

namespace hsm::collector::large_files
{
    namespace
    {
        constexpr int kVersion = 1;
        constexpr std::size_t kMaxEntries = 10000;

        // The wire's 7-digit UTC text back to ticks; false for anything else.
        bool ParseTicksText(const std::string& text, std::int64_t& ticks)
        {
            // Round trip through the formatter's inverse: the state is written by FormatTicks only.
            if (text.size() != 28 || text[4] != '-' || text[7] != '-' || text[10] != 'T' || text[13] != ':' || text[16] != ':' || text[19] != '.' ||
                text[27] != 'Z')
                return false;
            const auto digits = [&text](std::size_t at, std::size_t count, std::int64_t& value) {
                value = 0;
                for (std::size_t i = at; i < at + count; ++i)
                {
                    if (text[i] < '0' || text[i] > '9')
                        return false;
                    value = value * 10 + (text[i] - '0');
                }
                return true;
            };
            std::int64_t y = 0, mo = 0, d = 0, h = 0, mi = 0, s = 0, f = 0;
            if (!digits(0, 4, y) || !digits(5, 2, mo) || !digits(8, 2, d) || !digits(11, 2, h) || !digits(14, 2, mi) || !digits(17, 2, s) || !digits(20, 7, f))
                return false;
            if (mo < 1 || mo > 12 || d < 1 || d > 31)
                return false;
            std::int64_t yy = y - (mo <= 2 ? 1 : 0);
            const std::int64_t era = (yy >= 0 ? yy : yy - 399) / 400;
            const std::int64_t yoe = yy - era * 400;
            const std::int64_t doy = (153 * (mo + (mo > 2 ? -3 : 9)) + 2) / 5 + d - 1;
            const std::int64_t doe = yoe * 365 + yoe / 4 - yoe / 100 + doy;
            const std::int64_t days = era * 146097 + doe - 719468;
            ticks = ((days * 86400 + h * 3600 + mi * 60 + s) * kTicksPerSecond) + f;
            return FileConnection::FormatTicks(ticks) == text;
        }

        nlohmann::json KeyJson(const FileKey& key)
        {
            nlohmann::json entry = nlohmann::json::object();
            entry["name"] = key.name;
            entry["size"] = key.size;
            entry["modified"] = FileConnection::FormatTicks(key.modified_ticks);
            if (!key.identity.empty())
                entry["source"] = key.identity;
            return entry;
        }

        bool ReadKey(const nlohmann::json& entry, FileKey& key)
        {
            if (!entry.is_object())
                return false;
            const auto name = entry.find("name");
            const auto size = entry.find("size");
            const auto modified = entry.find("modified");
            if (name == entry.end() || !name->is_string() || size == entry.end() || !size->is_number_integer() || modified == entry.end() || !modified->is_string())
                return false;
            key.name = name->get<std::string>();
            key.size = size->get<std::int64_t>();
            if (!ParseTicksText(modified->get<std::string>(), key.modified_ticks))
                return false;
            const auto source = entry.find("source");
            key.identity = source != entry.end() && source->is_string() ? source->get<std::string>() : std::string();
            return true;
        }

        std::string CorruptName(const std::string& file)
        {
            const auto now = std::chrono::system_clock::now().time_since_epoch();
            const auto ticks = std::chrono::duration_cast<std::chrono::microseconds>(now).count() * 10;
            std::string stamp = FileConnection::FormatTicks(ticks);
            std::replace(stamp.begin(), stamp.end(), ':', '-');
            return file + ".corrupt-" + stamp;
        }

        // Renames a file that cannot be read as state; returns the note to report.
        std::string SetAside(const std::string& file, const std::string& why)
        {
            const std::string aside = CorruptName(file);
            std::string error;
            const bool moved = RenameFile(file, aside, error);
            if (!moved)
                RemoveFile(file, error);
            return "the large file state file " + file + " could not be read (" + why + "); it was " + (moved ? "renamed to " + aside : std::string("removed")) +
                   " and the collector starts with an empty state: a kept file may be offered again (the server answers \"stored\" for a file it still has)";
        }
    } // namespace

    std::string Sha16(const std::string& text)
    {
        Sha256Digest digest{};
        if (!Sha256Of(text.data(), text.size(), digest))
            return std::string(16, '0');
        return ToHex(digest.data(), 8);
    }

    FolderState::FolderState(std::string file, std::string folder, std::string mask, std::string sensor)
        : file_(std::move(file)), folder_(std::move(folder)), mask_(std::move(mask)), sensor_(std::move(sensor))
    {
    }

    LockedStateFile::Result FolderState::Load(std::string& note, std::string& error)
    {
        delivered_.clear();
        given_up_.clear();
        const auto locked = lock_.Lock(file_, error);
        if (locked != LockedStateFile::Result::Ok)
            return locked;
        std::string content;
        bool missing = false;
        if (!lock_.Read(content, missing, error))
        {
            if (missing)
                return LockedStateFile::Result::Ok;
            note = SetAside(file_, error);
            return LockedStateFile::Result::Ok;
        }
        const auto json = nlohmann::json::parse(content, nullptr, /*allow_exceptions=*/false);
        const auto version = json.is_object() ? json.find("version") : json.end();
        if (!json.is_object() || version == json.end() || !version->is_number_integer() || version->get<int>() != kVersion)
        {
            note = SetAside(file_, json.is_object() ? "an unknown version" : "not JSON");
            return LockedStateFile::Result::Ok;
        }
        try
        {
            const auto delivered = json.find("delivered");
            if (delivered != json.end() && delivered->is_array())
                for (const auto& entry : *delivered)
                {
                    DeliveredEntry item;
                    if (!ReadKey(entry, item.key))
                        throw std::runtime_error("a delivered entry without its facts");
                    const auto stored = entry.find("stored");
                    if (stored != entry.end() && stored->is_string())
                        ParseTicksText(stored->get<std::string>(), item.stored_ticks);
                    delivered_.push_back(std::move(item));
                }
            const auto given_up = json.find("givenUp");
            if (given_up != json.end() && given_up->is_array())
                for (const auto& entry : *given_up)
                {
                    GivenUpEntry item;
                    if (!ReadKey(entry, item.key))
                        throw std::runtime_error("a given-up entry without its facts");
                    const auto reason = entry.find("reason");
                    item.reason = reason != entry.end() && reason->is_string() ? reason->get<std::string>() : std::string();
                    given_up_.push_back(std::move(item));
                }
        }
        catch (const std::bad_alloc&)
        {
            throw;
        }
        catch (const std::exception& ex)
        {
            delivered_.clear();
            given_up_.clear();
            note = SetAside(file_, ex.what());
        }
        return LockedStateFile::Result::Ok;
    }

    void FolderState::Unload() noexcept
    {
        lock_.Unlock();
        delivered_.clear();
        given_up_.clear();
    }

    const DeliveredEntry* FolderState::FindDelivered(const FileKey& key) const
    {
        for (const auto& entry : delivered_)
            if (entry.key == key)
                return &entry;
        return nullptr;
    }

    const GivenUpEntry* FolderState::FindGivenUp(const FileKey& key) const
    {
        for (const auto& entry : given_up_)
            if (entry.key == key)
                return &entry;
        return nullptr;
    }

    void FolderState::SetDelivered(const FileKey& key, std::int64_t stored_ticks)
    {
        Forget(key);
        delivered_.push_back(DeliveredEntry{ key, stored_ticks });
        if (delivered_.size() > kMaxEntries)
            delivered_.erase(delivered_.begin());
    }

    void FolderState::SetGivenUp(const FileKey& key, const std::string& reason)
    {
        Forget(key);
        given_up_.push_back(GivenUpEntry{ key, reason });
        if (given_up_.size() > kMaxEntries)
            given_up_.erase(given_up_.begin());
    }

    void FolderState::Forget(const FileKey& key)
    {
        delivered_.erase(std::remove_if(delivered_.begin(), delivered_.end(), [&key](const DeliveredEntry& e) { return e.key == key; }), delivered_.end());
        given_up_.erase(std::remove_if(given_up_.begin(), given_up_.end(), [&key](const GivenUpEntry& e) { return e.key == key; }), given_up_.end());
    }

    bool FolderState::Prune(const std::set<std::string>& present)
    {
        const auto before = delivered_.size() + given_up_.size();
        delivered_.erase(std::remove_if(delivered_.begin(), delivered_.end(), [&present](const DeliveredEntry& e) { return present.count(e.key.name) == 0; }),
                         delivered_.end());
        given_up_.erase(std::remove_if(given_up_.begin(), given_up_.end(), [&present](const GivenUpEntry& e) { return present.count(e.key.name) == 0; }),
                        given_up_.end());
        return delivered_.size() + given_up_.size() != before;
    }

    bool FolderState::Save(std::string& error)
    {
        if (!lock_.Locked())
        {
            error = "the state file is not locked";
            return false;
        }
        nlohmann::json json = nlohmann::json::object();
        json["version"] = kVersion;
        json["folder"] = folder_;
        json["mask"] = mask_;
        json["sensor"] = sensor_;
        nlohmann::json delivered = nlohmann::json::array();
        for (const auto& entry : delivered_)
        {
            auto item = KeyJson(entry.key);
            if (entry.stored_ticks != 0)
                item["stored"] = FileConnection::FormatTicks(entry.stored_ticks);
            delivered.push_back(std::move(item));
        }
        nlohmann::json given_up = nlohmann::json::array();
        for (const auto& entry : given_up_)
        {
            auto item = KeyJson(entry.key);
            item["reason"] = entry.reason;
            given_up.push_back(std::move(item));
        }
        json["delivered"] = std::move(delivered);
        json["givenUp"] = std::move(given_up);
        return lock_.Replace(json.dump(), error);
    }

    UploadRecord::UploadRecord(std::string json_file)
        : file_(std::move(json_file))
    {
    }

    LockedStateFile::Result UploadRecord::Load(bool& exists, std::string& note, std::string& error)
    {
        exists = false;
        facts_ = UploadFacts{};
        digests_.clear();
        digest_file_.Close();
        const auto locked = lock_.Lock(file_, error);
        if (locked != LockedStateFile::Result::Ok)
            return locked;
        std::string content;
        bool missing = false;
        if (!lock_.Read(content, missing, error))
        {
            if (!missing)
                note = SetAside(file_, error);
            return LockedStateFile::Result::Ok;
        }
        const auto json = nlohmann::json::parse(content, nullptr, /*allow_exceptions=*/false);
        const auto version = json.is_object() ? json.find("version") : json.end();
        if (!json.is_object() || version == json.end() || !version->is_number_integer() || version->get<int>() != kVersion)
        {
            note = SetAside(file_, json.is_object() ? "an unknown version" : "not JSON");
            return LockedStateFile::Result::Ok;
        }
        const auto text = [&json](const char* name) {
            const auto found = json.find(name);
            return found != json.end() && found->is_string() ? found->get<std::string>() : std::string();
        };
        const auto number = [&json](const char* name) {
            const auto found = json.find(name);
            return found != json.end() && found->is_number_integer() ? found->get<std::int64_t>() : std::int64_t{ 0 };
        };
        if (!ReadKey(json, facts_.key) || text("path").empty() || text("sensor").empty())
        {
            facts_ = UploadFacts{};
            note = SetAside(file_, "an upload record without its facts");
            return LockedStateFile::Result::Ok;
        }
        facts_.path = text("path");
        facts_.sensor = text("sensor");
        facts_.upload = text("upload");
        {
            const auto secure = json.find("secure");
            facts_.secure = secure == json.end() || !secure->is_boolean() || secure->get<bool>();
        }
        facts_.piece_bytes = number("pieceBytes");
        facts_.discards = static_cast<int>(number("discards"));
        facts_.checksum_mismatches = static_cast<int>(number("checksumMismatches"));
        facts_.protocol_closes = static_cast<int>(number("protocolCloses"));
        exists = true;
        return LockedStateFile::Result::Ok;
    }

    bool UploadRecord::Save(std::string& error)
    {
        auto json = KeyJson(facts_.key);
        json["version"] = kVersion;
        json["path"] = facts_.path;
        json["sensor"] = facts_.sensor;
        json["upload"] = facts_.upload;
        json["secure"] = facts_.secure;
        json["pieceBytes"] = facts_.piece_bytes;
        json["discards"] = facts_.discards;
        json["checksumMismatches"] = facts_.checksum_mismatches;
        json["protocolCloses"] = facts_.protocol_closes;
        return lock_.Replace(json.dump(), error);
    }

    void UploadRecord::Remove() noexcept
    {
        try
        {
            digest_file_.Close();
            std::string error;
            RemoveFile(file_.substr(0, file_.size() - 5) + ".digests", error);
            lock_.Remove(error);
        }
        catch (...)
        {
        }
        Unload();
    }

    void UploadRecord::Unload() noexcept
    {
        digest_file_.Close();
        lock_.Unlock();
        digests_.clear();
    }

    bool UploadRecord::OpenDigests(std::int64_t pieces, std::string& error)
    {
        digests_.clear();
        if (!digest_file_.Open(file_.substr(0, file_.size() - 5) + ".digests", pieces, 32, error))
        {
            digests_.assign(static_cast<std::size_t>(pieces) * 32, 0);
            return false;
        }
        if (!digest_file_.ReadAll(digests_, error))
            return false;
        return true;
    }

    bool UploadRecord::Digest(std::int64_t index, Sha256Digest& out) const
    {
        if (index < 0 || index >= Pieces())
            return false;
        bool any = false;
        for (std::size_t i = 0; i < 32; ++i)
        {
            out[i] = digests_[static_cast<std::size_t>(index) * 32 + i];
            any = any || out[i] != 0;
        }
        return any;
    }

    bool UploadRecord::SetDigest(std::int64_t index, const Sha256Digest& digest, std::string& error)
    {
        if (index < 0 || index >= Pieces())
        {
            error = "a digest outside the list";
            return false;
        }
        std::copy(digest.begin(), digest.end(), digests_.begin() + static_cast<std::ptrdiff_t>(index * 32));
        if (!digest_file_.IsOpen())
            return true; // kept in memory: a restart reads the piece again
        return digest_file_.Write(index, digest.data(), error);
    }

    std::vector<std::string> ListUploadRecords(const std::string& state_folder)
    {
        std::vector<std::string> names;
        std::vector<FolderEntry> entries;
        std::string error;
        if (ListFolder(state_folder, entries, error) != ListResult::Ok)
            return names;
        for (const auto& entry : entries)
            if (entry.name_valid && !entry.is_link && entry.name.rfind("upload-", 0) == 0 && entry.name.size() > 5 &&
                entry.name.compare(entry.name.size() - 5, 5, ".json") == 0)
                names.push_back(JoinPath(state_folder, entry.name));
        return names;
    }
} // namespace hsm::collector::large_files
