#include "hsm_large_file_platform.hpp"

#include <algorithm>
#include <atomic>
#include <chrono>
#include <cstdio>
#include <cstring>
#include <filesystem>
#include <random>
#include <system_error>

#if defined(_WIN32)
#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#ifndef NOMINMAX
#define NOMINMAX
#endif
#include <windows.h>
#include <knownfolders.h>
#include <shlobj.h>
#else
#include <cerrno>
#include <cstdlib>
#include <dirent.h>
#include <fcntl.h>
#include <sys/file.h>
#include <sys/resource.h>
#include <sys/stat.h>
#include <sys/types.h>
#include <unistd.h>
#if defined(__linux__)
#include <sys/syscall.h>
#endif
#endif

namespace hsm::collector::large_files
{
    namespace
    {
        constexpr std::size_t kReadSlice = 1024 * 1024;

        std::string UniqueSuffix()
        {
            static std::atomic<std::uint64_t> counter{ 0 };
            std::random_device device;
            const std::uint64_t random = (static_cast<std::uint64_t>(device()) << 32) ^ device();
            return std::to_string(ProcessId()) + "." + std::to_string(counter.fetch_add(1)) + "." + std::to_string(random % 1000000007ULL);
        }

#if defined(_WIN32)
        // 100 ns since 1601 (FILETIME) to 100 ns since 1970.
        constexpr std::int64_t kFiletimeTo1970 = 116444736000000000LL;

        std::wstring Wide(const std::string& text, bool& ok)
        {
            ok = true;
            if (text.empty())
                return std::wstring();
            const int length = MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, text.data(), static_cast<int>(text.size()), nullptr, 0);
            if (length <= 0)
            {
                ok = false;
                return std::wstring();
            }
            std::wstring wide(static_cast<std::size_t>(length), L'\0');
            MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, text.data(), static_cast<int>(text.size()), wide.data(), length);
            return wide;
        }

        std::string Narrow(const wchar_t* text, std::size_t size, bool& ok)
        {
            ok = true;
            if (size == 0)
                return std::string();
            const int length = WideCharToMultiByte(CP_UTF8, WC_ERR_INVALID_CHARS, text, static_cast<int>(size), nullptr, 0, nullptr, nullptr);
            if (length <= 0)
            {
                ok = false;
                return std::string();
            }
            std::string narrow(static_cast<std::size_t>(length), '\0');
            WideCharToMultiByte(CP_UTF8, WC_ERR_INVALID_CHARS, text, static_cast<int>(size), narrow.data(), length, nullptr, nullptr);
            return narrow;
        }

        std::string Narrow(const std::wstring& text)
        {
            bool ok = true;
            return Narrow(text.data(), text.size(), ok);
        }

        // An absolute UTF-8 path in the form that passes MAX_PATH: \\?\C:\... or \\?\UNC\server\share\...
        std::wstring LongPath(const std::string& path, bool& ok)
        {
            std::wstring wide = Wide(path, ok);
            if (!ok)
                return std::wstring();
            std::replace(wide.begin(), wide.end(), L'/', L'\\');
            if (wide.rfind(L"\\\\?\\", 0) == 0)
                return wide;
            if (wide.rfind(L"\\\\", 0) == 0)
                return L"\\\\?\\UNC\\" + wide.substr(2);
            return L"\\\\?\\" + wide;
        }

        std::string LastErrorText(const char* what, DWORD code)
        {
            wchar_t* message = nullptr;
            const DWORD length = FormatMessageW(FORMAT_MESSAGE_ALLOCATE_BUFFER | FORMAT_MESSAGE_FROM_SYSTEM | FORMAT_MESSAGE_IGNORE_INSERTS, nullptr, code, 0,
                                                reinterpret_cast<LPWSTR>(&message), 0, nullptr);
            std::string text = std::string(what) + " failed (Windows error " + std::to_string(code);
            if (length > 0 && message != nullptr)
            {
                std::wstring words(message, length);
                while (!words.empty() && (words.back() == L'\r' || words.back() == L'\n' || words.back() == L' ' || words.back() == L'.'))
                    words.pop_back();
                text += ": " + Narrow(words);
            }
            if (message != nullptr)
                LocalFree(message);
            return text + ")";
        }

        std::int64_t TicksOf(const LARGE_INTEGER& filetime)
        {
            return static_cast<std::int64_t>(filetime.QuadPart) - kFiletimeTo1970;
        }

        std::int64_t TicksOf(const FILETIME& filetime)
        {
            LARGE_INTEGER value;
            value.LowPart = filetime.dwLowDateTime;
            value.HighPart = static_cast<LONG>(filetime.dwHighDateTime);
            return TicksOf(value);
        }

        bool HandleFacts(HANDLE handle, SourceFacts& out, std::string& error)
        {
            FILE_BASIC_INFO basic{};
            FILE_STANDARD_INFO standard{};
            FILE_ID_INFO id{};
            if (!GetFileInformationByHandleEx(handle, FileBasicInfo, &basic, sizeof basic))
            {
                error = LastErrorText("reading the file's time", GetLastError());
                return false;
            }
            if (!GetFileInformationByHandleEx(handle, FileStandardInfo, &standard, sizeof standard))
            {
                error = LastErrorText("reading the file's size", GetLastError());
                return false;
            }
            out = SourceFacts{};
            out.size = static_cast<std::int64_t>(standard.EndOfFile.QuadPart);
            out.modified_ticks = TicksOf(basic.LastWriteTime);
            if (GetFileInformationByHandleEx(handle, FileIdInfo, &id, sizeof id))
            {
                bool zero = true;
                for (unsigned char byte : id.FileId.Identifier)
                    zero = zero && byte == 0;
                if (!zero)
                {
                    char volume[17];
                    std::snprintf(volume, sizeof volume, "%016llx", static_cast<unsigned long long>(id.VolumeSerialNumber));
                    static const char digits[] = "0123456789abcdef";
                    std::string file;
                    for (unsigned char byte : id.FileId.Identifier)
                    {
                        file.push_back(digits[byte >> 4]);
                        file.push_back(digits[byte & 0x0F]);
                    }
                    out.identity = std::string("vol:") + volume + "/file:" + file;
                }
            }
            return true;
        }

        HANDLE AsHandle(void* handle)
        {
            return handle == nullptr ? INVALID_HANDLE_VALUE : static_cast<HANDLE>(handle);
        }
#else
        std::string ErrnoText(const char* what, int code)
        {
            return std::string(what) + " failed (" + std::strerror(code) + ")";
        }

        std::int64_t TicksOf(const struct stat& status)
        {
#if defined(__APPLE__)
            const auto& time = status.st_mtimespec;
#else
            const auto& time = status.st_mtim;
#endif
            return static_cast<std::int64_t>(time.tv_sec) * kTicksPerSecond + static_cast<std::int64_t>(time.tv_nsec) / 100;
        }

        void FactsOf(const struct stat& status, SourceFacts& out)
        {
            out = SourceFacts{};
            out.size = static_cast<std::int64_t>(status.st_size);
            out.modified_ticks = TicksOf(status);
            char inode[32];
            std::snprintf(inode, sizeof inode, "ino:%llx", static_cast<unsigned long long>(status.st_ino));
            out.identity = inode;
            out.device = static_cast<std::uint64_t>(status.st_dev);
        }

        bool WriteAllFd(int fd, const char* data, std::size_t size, std::string& error)
        {
            while (size > 0)
            {
                const ssize_t written = ::write(fd, data, size);
                if (written < 0)
                {
                    if (errno == EINTR)
                        continue;
                    error = ErrnoText("writing a state file", errno);
                    return false;
                }
                data += written;
                size -= static_cast<std::size_t>(written);
            }
            return true;
        }
#endif
    } // namespace

    // ---- Folders ----

    ListResult ListFolder(const std::string& folder, std::vector<FolderEntry>& out, std::string& error)
    {
        out.clear();
#if defined(_WIN32)
        bool ok = true;
        std::wstring pattern = LongPath(folder, ok);
        if (!ok)
        {
            error = "the folder's name is not valid UTF-8";
            return ListResult::Failed;
        }
        if (!pattern.empty() && pattern.back() != L'\\')
            pattern += L'\\';
        pattern += L'*';
        WIN32_FIND_DATAW data{};
        HANDLE find = FindFirstFileExW(pattern.c_str(), FindExInfoBasic, &data, FindExSearchNameMatch, nullptr, FIND_FIRST_EX_LARGE_FETCH);
        if (find == INVALID_HANDLE_VALUE)
        {
            const DWORD code = GetLastError();
            if (code == ERROR_FILE_NOT_FOUND)
                return ListResult::Ok;
            if (code == ERROR_PATH_NOT_FOUND || code == ERROR_DIRECTORY || code == ERROR_BAD_NETPATH || code == ERROR_BAD_NET_NAME)
            {
                error = LastErrorText("listing the folder", code);
                return ListResult::Missing;
            }
            error = LastErrorText("listing the folder", code);
            return ListResult::Failed;
        }
        do
        {
            if ((data.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) != 0)
                continue;
            FolderEntry entry;
            bool valid = true;
            entry.name = Narrow(data.cFileName, wcslen(data.cFileName), valid);
            entry.name_valid = valid;
            if (!valid)
                entry.name.clear();
            entry.is_link = (data.dwFileAttributes & FILE_ATTRIBUTE_REPARSE_POINT) != 0;
            entry.size = static_cast<std::int64_t>((static_cast<std::uint64_t>(data.nFileSizeHigh) << 32) | data.nFileSizeLow);
            entry.modified_ticks = TicksOf(data.ftLastWriteTime);
            out.push_back(std::move(entry));
        } while (FindNextFileW(find, &data));
        const DWORD code = GetLastError();
        FindClose(find);
        if (code != ERROR_NO_MORE_FILES)
        {
            error = LastErrorText("listing the folder", code);
            return ListResult::Failed;
        }
        return ListResult::Ok;
#else
        DIR* dir = ::opendir(folder.c_str());
        if (dir == nullptr)
        {
            const int code = errno;
            error = ErrnoText("listing the folder", code);
            return code == ENOENT || code == ENOTDIR ? ListResult::Missing : ListResult::Failed;
        }
        const int dir_fd = ::dirfd(dir);
        for (;;)
        {
            errno = 0;
            const dirent* entry = ::readdir(dir);
            if (entry == nullptr)
            {
                const int code = errno;
                ::closedir(dir);
                if (code != 0)
                {
                    error = ErrnoText("listing the folder", code);
                    return ListResult::Failed;
                }
                return ListResult::Ok;
            }
            const std::string name = entry->d_name;
            if (name == "." || name == "..")
                continue;
            struct stat status{};
            if (::fstatat(dir_fd, entry->d_name, &status, AT_SYMLINK_NOFOLLOW) != 0)
                continue; // gone between the two calls
            FolderEntry item;
            item.raw_name = name;
            item.is_link = S_ISLNK(status.st_mode);
            if (!item.is_link && !S_ISREG(status.st_mode))
                continue;
            item.name_valid = IsValidUtf8(name);
            if (item.name_valid)
                item.name = name;
            item.size = static_cast<std::int64_t>(status.st_size);
            item.modified_ticks = TicksOf(status);
            out.push_back(std::move(item));
        }
#endif
    }

    // ---- The source file ----

    SourceFile::~SourceFile()
    {
        Close();
    }

    bool SourceFile::IsOpen() const noexcept
    {
#if defined(_WIN32)
        return handle_ != nullptr;
#else
        return fd_ >= 0;
#endif
    }

    void SourceFile::Close() noexcept
    {
#if defined(_WIN32)
        if (handle_ != nullptr)
            CloseHandle(static_cast<HANDLE>(handle_));
        handle_ = nullptr;
#else
        if (fd_ >= 0)
            ::close(fd_);
        fd_ = -1;
#endif
    }

    OpenResult SourceFile::Open(const std::string& path, std::string& error)
    {
        Close();
#if defined(_WIN32)
        bool ok = true;
        const std::wstring wide = LongPath(path, ok);
        if (!ok)
        {
            error = "the path is not valid UTF-8";
            return OpenResult::Failed;
        }
        HANDLE handle = CreateFileW(wide.c_str(), GENERIC_READ | DELETE, FILE_SHARE_READ, nullptr, OPEN_EXISTING,
                                    FILE_ATTRIBUTE_NORMAL | FILE_FLAG_OPEN_REPARSE_POINT | FILE_FLAG_SEQUENTIAL_SCAN, nullptr);
        if (handle == INVALID_HANDLE_VALUE)
        {
            const DWORD code = GetLastError();
            error = LastErrorText("opening the file", code);
            if (code == ERROR_SHARING_VIOLATION || code == ERROR_LOCK_VIOLATION)
                return OpenResult::Busy;
            if (code == ERROR_FILE_NOT_FOUND || code == ERROR_PATH_NOT_FOUND)
                return OpenResult::Missing;
            if (code == ERROR_ACCESS_DENIED)
                return OpenResult::Denied;
            return OpenResult::Failed;
        }
        FILE_ATTRIBUTE_TAG_INFO tag{};
        if (!GetFileInformationByHandleEx(handle, FileAttributeTagInfo, &tag, sizeof tag))
        {
            error = LastErrorText("reading the file's attributes", GetLastError());
            CloseHandle(handle);
            return OpenResult::Failed;
        }
        if ((tag.FileAttributes & FILE_ATTRIBUTE_REPARSE_POINT) != 0)
        {
            error = "the file is a link or a reparse point";
            CloseHandle(handle);
            return OpenResult::Link;
        }
        if ((tag.FileAttributes & FILE_ATTRIBUTE_DIRECTORY) != 0)
        {
            error = "the path is a folder";
            CloseHandle(handle);
            return OpenResult::Failed;
        }
        handle_ = handle;
        return OpenResult::Ok;
#else
        // O_NONBLOCK: a FIFO put in the place of a file does not block the open (S_ISREG then refuses it).
        const int fd = ::open(path.c_str(), O_RDONLY | O_NOFOLLOW | O_CLOEXEC | O_NONBLOCK);
        if (fd < 0)
        {
            const int code = errno;
            error = ErrnoText("opening the file", code);
            if (code == ELOOP)
                return OpenResult::Link;
            if (code == ENOENT || code == ENOTDIR)
                return OpenResult::Missing;
            if (code == EACCES || code == EPERM)
                return OpenResult::Denied;
            return OpenResult::Failed;
        }
        struct stat status{};
        if (::fstat(fd, &status) != 0 || !S_ISREG(status.st_mode))
        {
            error = "the path is not a regular file";
            ::close(fd);
            return OpenResult::Failed;
        }
        fd_ = fd;
        return OpenResult::Ok;
#endif
    }

    bool SourceFile::Facts(SourceFacts& out, std::string& error) const
    {
        if (!IsOpen())
        {
            error = "the file is not open";
            return false;
        }
#if defined(_WIN32)
        return HandleFacts(static_cast<HANDLE>(handle_), out, error);
#else
        struct stat status{};
        if (::fstat(fd_, &status) != 0)
        {
            error = ErrnoText("reading the file's facts", errno);
            return false;
        }
        FactsOf(status, out);
        return true;
#endif
    }

    bool SourceFile::ReadAt(std::int64_t offset, std::uint8_t* buffer, std::size_t size, const std::function<bool()>& stop, std::string& error)
    {
        if (!IsOpen())
        {
            error = "the file is not open";
            return false;
        }
        std::size_t done = 0;
        while (done < size)
        {
            if (stop && stop())
            {
                error = "stopped";
                return false;
            }
            const std::size_t want = (std::min)(kReadSlice, size - done);
            const std::int64_t at = offset + static_cast<std::int64_t>(done);
#if defined(_WIN32)
            OVERLAPPED position{};
            position.Offset = static_cast<DWORD>(static_cast<std::uint64_t>(at) & 0xFFFFFFFFu);
            position.OffsetHigh = static_cast<DWORD>(static_cast<std::uint64_t>(at) >> 32);
            DWORD read = 0;
            if (!ReadFile(static_cast<HANDLE>(handle_), buffer + done, static_cast<DWORD>(want), &read, &position))
            {
                const DWORD code = GetLastError();
                if (code != ERROR_HANDLE_EOF)
                {
                    error = code == ERROR_OPERATION_ABORTED ? std::string("stopped") : LastErrorText("reading the file", code);
                    return false;
                }
            }
            if (read == 0)
            {
                error = "the file is shorter than when it was taken";
                return false;
            }
            done += read;
#else
            const ssize_t read = ::pread(fd_, buffer + done, want, static_cast<off_t>(at));
            if (read < 0)
            {
                if (errno == EINTR)
                    continue;
                error = ErrnoText("reading the file", errno);
                return false;
            }
            if (read == 0)
            {
                error = "the file is shorter than when it was taken";
                return false;
            }
            done += static_cast<std::size_t>(read);
#endif
        }
        return true;
    }

    DeleteResult SourceFile::DeleteThis(const std::string& path, std::string& error)
    {
        if (!IsOpen())
        {
            error = "the file is not open";
            return DeleteResult::Failed;
        }
#if defined(_WIN32)
        (void)path;
        FILE_DISPOSITION_INFO disposition{};
        disposition.DeleteFile = TRUE;
        const bool ok = SetFileInformationByHandle(static_cast<HANDLE>(handle_), FileDispositionInfo, &disposition, sizeof disposition) != 0;
        if (!ok)
            error = LastErrorText("deleting the file", GetLastError());
        Close(); // the delete happens when the last handle closes: ours is the only one
        return ok ? DeleteResult::Deleted : DeleteResult::Failed;
#else
        struct stat open_status{};
        struct stat path_status{};
        if (::fstat(fd_, &open_status) != 0)
        {
            error = ErrnoText("reading the open file", errno);
            Close();
            return DeleteResult::Failed;
        }
        if (::lstat(path.c_str(), &path_status) != 0 || !S_ISREG(path_status.st_mode) || path_status.st_dev != open_status.st_dev ||
            path_status.st_ino != open_status.st_ino)
        {
            error = "the file at the path is no longer the file that was sent";
            Close();
            return DeleteResult::Changed;
        }
        // The descriptor stays open over the unlink: the inode cannot be reused by a replacing file
        // between the comparison and the unlink.
        const bool ok = ::unlink(path.c_str()) == 0;
        if (!ok)
            error = ErrnoText("deleting the file", errno);
        Close();
        return ok ? DeleteResult::Deleted : DeleteResult::Failed;
#endif
    }

    bool PathFacts(const std::string& path, SourceFacts& out, std::string& error)
    {
#if defined(_WIN32)
        bool ok = true;
        const std::wstring wide = LongPath(path, ok);
        if (!ok)
        {
            error = "the path is not valid UTF-8";
            return false;
        }
        // Read access is not needed for the facts: no reader or writer is disturbed.
        HANDLE handle = CreateFileW(wide.c_str(), FILE_READ_ATTRIBUTES, FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE, nullptr, OPEN_EXISTING,
                                    FILE_FLAG_OPEN_REPARSE_POINT | FILE_FLAG_BACKUP_SEMANTICS, nullptr);
        if (handle == INVALID_HANDLE_VALUE)
        {
            error = LastErrorText("reading the file's facts", GetLastError());
            return false;
        }
        FILE_ATTRIBUTE_TAG_INFO tag{};
        const bool plain = GetFileInformationByHandleEx(handle, FileAttributeTagInfo, &tag, sizeof tag) &&
                           (tag.FileAttributes & (FILE_ATTRIBUTE_REPARSE_POINT | FILE_ATTRIBUTE_DIRECTORY)) == 0;
        const bool read = plain && HandleFacts(handle, out, error);
        CloseHandle(handle);
        if (!plain)
            error = "the path is not a regular file";
        return read;
#else
        struct stat status{};
        if (::lstat(path.c_str(), &status) != 0)
        {
            error = ErrnoText("reading the file's facts", errno);
            return false;
        }
        if (!S_ISREG(status.st_mode))
        {
            error = "the path is not a regular file";
            return false;
        }
        FactsOf(status, out);
        return true;
#endif
    }

    // ---- State files ----

    LockedStateFile::~LockedStateFile()
    {
        Unlock();
    }

    bool LockedStateFile::Locked() const noexcept
    {
#if defined(_WIN32)
        return lock_ != nullptr;
#else
        return lock_ >= 0;
#endif
    }

    void LockedStateFile::Unlock() noexcept
    {
#if defined(_WIN32)
        if (lock_ != nullptr)
            CloseHandle(static_cast<HANDLE>(lock_));
        lock_ = nullptr;
#else
        if (lock_ >= 0)
            ::close(lock_); // releases the flock
        lock_ = -1;
#endif
    }

    LockedStateFile::Result LockedStateFile::Lock(const std::string& path, std::string& error)
    {
        Unlock();
        path_ = path;
        const std::string lock_path = path + ".lock";
#if defined(_WIN32)
        bool ok = true;
        const std::wstring wide = LongPath(lock_path, ok);
        if (!ok)
        {
            error = "the path is not valid UTF-8";
            return Result::Failed;
        }
        HANDLE handle = CreateFileW(wide.c_str(), GENERIC_READ | GENERIC_WRITE, 0, nullptr, OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
        if (handle == INVALID_HANDLE_VALUE)
        {
            const DWORD code = GetLastError();
            error = LastErrorText("locking a state file", code);
            return code == ERROR_SHARING_VIOLATION || code == ERROR_LOCK_VIOLATION ? Result::Busy : Result::Failed;
        }
        lock_ = handle;
        return Result::Ok;
#else
        const int fd = ::open(lock_path.c_str(), O_RDWR | O_CREAT | O_CLOEXEC, 0600);
        if (fd < 0)
        {
            error = ErrnoText("locking a state file", errno);
            return Result::Failed;
        }
        if (::flock(fd, LOCK_EX | LOCK_NB) != 0)
        {
            const int code = errno;
            ::close(fd);
            error = ErrnoText("locking a state file", code);
            return code == EWOULDBLOCK ? Result::Busy : Result::Failed;
        }
        lock_ = fd;
        return Result::Ok;
#endif
    }

    bool LockedStateFile::Read(std::string& content, bool& missing, std::string& error) const
    {
        content.clear();
        missing = false;
#if defined(_WIN32)
        bool ok = true;
        const std::wstring wide = LongPath(path_, ok);
        if (!ok)
        {
            error = "the path is not valid UTF-8";
            return false;
        }
        HANDLE handle = CreateFileW(wide.c_str(), GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_DELETE, nullptr, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
        if (handle == INVALID_HANDLE_VALUE)
        {
            const DWORD code = GetLastError();
            missing = code == ERROR_FILE_NOT_FOUND || code == ERROR_PATH_NOT_FOUND;
            error = LastErrorText("reading a state file", code);
            return false;
        }
        LARGE_INTEGER size{};
        GetFileSizeEx(handle, &size);
        if (size.QuadPart > 256LL * 1024 * 1024)
        {
            CloseHandle(handle);
            error = "a state file larger than 256 MiB";
            return false;
        }
        content.resize(static_cast<std::size_t>(size.QuadPart));
        DWORD read = 0;
        const bool done = content.empty() || (ReadFile(handle, content.data(), static_cast<DWORD>(content.size()), &read, nullptr) && read == content.size());
        if (!done)
            error = LastErrorText("reading a state file", GetLastError());
        CloseHandle(handle);
        return done;
#else
        const int fd = ::open(path_.c_str(), O_RDONLY | O_CLOEXEC);
        if (fd < 0)
        {
            missing = errno == ENOENT;
            error = ErrnoText("reading a state file", errno);
            return false;
        }
        char buffer[65536];
        for (;;)
        {
            const ssize_t read = ::read(fd, buffer, sizeof buffer);
            if (read < 0)
            {
                if (errno == EINTR)
                    continue;
                error = ErrnoText("reading a state file", errno);
                ::close(fd);
                return false;
            }
            if (read == 0)
                break;
            content.append(buffer, static_cast<std::size_t>(read));
            if (content.size() > 256u * 1024 * 1024)
            {
                error = "a state file larger than 256 MiB";
                ::close(fd);
                return false;
            }
        }
        ::close(fd);
        return true;
#endif
    }

    bool LockedStateFile::Replace(const std::string& content, std::string& error)
    {
        const std::string temporary = path_ + "." + UniqueSuffix() + ".tmp";
#if defined(_WIN32)
        bool ok = true;
        const std::wstring wide_temporary = LongPath(temporary, ok);
        const std::wstring wide_path = ok ? LongPath(path_, ok) : std::wstring();
        if (!ok)
        {
            error = "the path is not valid UTF-8";
            return false;
        }
        HANDLE handle = CreateFileW(wide_temporary.c_str(), GENERIC_WRITE, 0, nullptr, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
        if (handle == INVALID_HANDLE_VALUE)
        {
            error = LastErrorText("writing a state file", GetLastError());
            return false;
        }
        DWORD written = 0;
        bool done = content.empty() || (WriteFile(handle, content.data(), static_cast<DWORD>(content.size()), &written, nullptr) && written == content.size());
        done = done && FlushFileBuffers(handle);
        if (!done)
            error = LastErrorText("writing a state file", GetLastError());
        CloseHandle(handle);
        if (done && !MoveFileExW(wide_temporary.c_str(), wide_path.c_str(), MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH))
        {
            error = LastErrorText("replacing a state file", GetLastError());
            done = false;
        }
        if (!done)
            DeleteFileW(wide_temporary.c_str());
        return done;
#else
        const int fd = ::open(temporary.c_str(), O_WRONLY | O_CREAT | O_TRUNC | O_CLOEXEC, 0600);
        if (fd < 0)
        {
            error = ErrnoText("writing a state file", errno);
            return false;
        }
        bool done = WriteAllFd(fd, content.data(), content.size(), error);
        if (done && ::fsync(fd) != 0)
        {
            error = ErrnoText("flushing a state file", errno);
            done = false;
        }
        ::close(fd);
        if (done && ::rename(temporary.c_str(), path_.c_str()) != 0)
        {
            error = ErrnoText("replacing a state file", errno);
            done = false;
        }
        if (!done)
        {
            ::unlink(temporary.c_str());
            return false;
        }
        const std::string folder = FolderOf(path_);
        const int dir = ::open(folder.empty() ? "." : folder.c_str(), O_RDONLY | O_CLOEXEC);
        if (dir >= 0)
        {
            ::fsync(dir); // the rename itself; a failure here loses at most this write after a power loss
            ::close(dir);
        }
        return true;
#endif
    }

    bool LockedStateFile::Remove(std::string& error)
    {
        return RemoveFile(path_, error);
    }

    // ---- The digest list ----

    RecordFile::~RecordFile()
    {
        Close();
    }

    bool RecordFile::IsOpen() const noexcept
    {
#if defined(_WIN32)
        return handle_ != nullptr;
#else
        return fd_ >= 0;
#endif
    }

    void RecordFile::Close() noexcept
    {
#if defined(_WIN32)
        if (handle_ != nullptr)
            CloseHandle(static_cast<HANDLE>(handle_));
        handle_ = nullptr;
#else
        if (fd_ >= 0)
            ::close(fd_);
        fd_ = -1;
#endif
    }

    bool RecordFile::Open(const std::string& path, std::int64_t count, std::size_t record_bytes, std::string& error)
    {
        Close();
        count_ = count;
        record_bytes_ = record_bytes;
        const std::int64_t size = count * static_cast<std::int64_t>(record_bytes);
#if defined(_WIN32)
        bool ok = true;
        const std::wstring wide = LongPath(path, ok);
        if (!ok)
        {
            error = "the path is not valid UTF-8";
            return false;
        }
        HANDLE handle = CreateFileW(wide.c_str(), GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ, nullptr, OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
        if (handle == INVALID_HANDLE_VALUE)
        {
            error = LastErrorText("opening the digest list", GetLastError());
            return false;
        }
        LARGE_INTEGER current{};
        GetFileSizeEx(handle, &current);
        if (current.QuadPart != size)
        {
            // Another length is another upload's list: zeros (every digest absent).
            LARGE_INTEGER zero{};
            LARGE_INTEGER target{};
            target.QuadPart = size;
            const bool sized = SetFilePointerEx(handle, zero, nullptr, FILE_BEGIN) && SetEndOfFile(handle) && SetFilePointerEx(handle, target, nullptr, FILE_BEGIN) &&
                               SetEndOfFile(handle);
            if (!sized)
            {
                error = LastErrorText("sizing the digest list", GetLastError());
                CloseHandle(handle);
                return false;
            }
        }
        handle_ = handle;
        return true;
#else
        const int fd = ::open(path.c_str(), O_RDWR | O_CREAT | O_CLOEXEC, 0600);
        if (fd < 0)
        {
            error = ErrnoText("opening the digest list", errno);
            return false;
        }
        struct stat status{};
        if (::fstat(fd, &status) != 0 || (status.st_size != size && (::ftruncate(fd, 0) != 0 || ::ftruncate(fd, static_cast<off_t>(size)) != 0)))
        {
            error = ErrnoText("sizing the digest list", errno);
            ::close(fd);
            return false;
        }
        fd_ = fd;
        return true;
#endif
    }

    bool RecordFile::ReadAll(std::vector<std::uint8_t>& out, std::string& error) const
    {
        out.assign(static_cast<std::size_t>(count_) * record_bytes_, 0);
        if (out.empty())
            return true;
#if defined(_WIN32)
        OVERLAPPED position{};
        DWORD read = 0;
        if (!ReadFile(static_cast<HANDLE>(handle_), out.data(), static_cast<DWORD>(out.size()), &read, &position) || read != out.size())
        {
            error = LastErrorText("reading the digest list", GetLastError());
            std::fill(out.begin(), out.end(), 0);
            return false;
        }
        return true;
#else
        std::size_t done = 0;
        while (done < out.size())
        {
            const ssize_t read = ::pread(fd_, out.data() + done, out.size() - done, static_cast<off_t>(done));
            if (read < 0 && errno == EINTR)
                continue;
            if (read <= 0)
            {
                error = ErrnoText("reading the digest list", errno);
                std::fill(out.begin(), out.end(), 0);
                return false;
            }
            done += static_cast<std::size_t>(read);
        }
        return true;
#endif
    }

    bool RecordFile::Write(std::int64_t index, const std::uint8_t* record, std::string& error)
    {
        if (!IsOpen() || index < 0 || index >= count_)
        {
            error = "a digest outside the list";
            return false;
        }
        const std::int64_t at = index * static_cast<std::int64_t>(record_bytes_);
#if defined(_WIN32)
        OVERLAPPED position{};
        position.Offset = static_cast<DWORD>(static_cast<std::uint64_t>(at) & 0xFFFFFFFFu);
        position.OffsetHigh = static_cast<DWORD>(static_cast<std::uint64_t>(at) >> 32);
        DWORD written = 0;
        if (!WriteFile(static_cast<HANDLE>(handle_), record, static_cast<DWORD>(record_bytes_), &written, &position) || written != record_bytes_)
        {
            error = LastErrorText("writing the digest list", GetLastError());
            return false;
        }
        return true;
#else
        std::size_t done = 0;
        while (done < record_bytes_)
        {
            const ssize_t written = ::pwrite(fd_, record + done, record_bytes_ - done, static_cast<off_t>(at + static_cast<std::int64_t>(done)));
            if (written < 0 && errno == EINTR)
                continue;
            if (written <= 0)
            {
                error = ErrnoText("writing the digest list", errno);
                return false;
            }
            done += static_cast<std::size_t>(written);
        }
        return true;
#endif
    }

    // ---- Paths ----

    namespace
    {
        std::filesystem::path FsPath(const std::string& utf8)
        {
#if defined(_WIN32)
            bool ok = true;
            return std::filesystem::path(Wide(utf8, ok));
#else
            return std::filesystem::path(utf8);
#endif
        }

        std::string FsString(const std::filesystem::path& path)
        {
#if defined(_WIN32)
            return Narrow(path.wstring());
#else
            return path.string();
#endif
        }
    } // namespace

    bool RemoveFile(const std::string& path, std::string& error)
    {
        std::error_code code;
        std::filesystem::remove(FsPath(path), code);
        if (code)
        {
            error = "removing " + path + " failed (" + code.message() + ")";
            return false;
        }
        return true;
    }

    bool RenameFile(const std::string& from, const std::string& to, std::string& error)
    {
        std::error_code code;
        std::filesystem::rename(FsPath(from), FsPath(to), code);
        if (code)
        {
            error = "renaming " + from + " failed (" + code.message() + ")";
            return false;
        }
        return true;
    }

    bool MakeFolders(const std::string& path, std::string& error)
    {
        std::error_code code;
        std::filesystem::create_directories(FsPath(path), code);
        if (code)
        {
            error = "creating the folder " + path + " failed (" + code.message() + ")";
            return false;
        }
        return true;
    }

    std::string AbsolutePath(const std::string& path)
    {
        try
        {
            std::error_code code;
            const auto absolute = std::filesystem::absolute(FsPath(path), code);
            if (code)
                return std::string();
            return FsString(absolute.lexically_normal());
        }
        catch (...)
        {
            return std::string();
        }
    }

    std::string JoinPath(const std::string& folder, const std::string& name)
    {
        if (folder.empty())
            return name;
        const char last = folder.back();
#if defined(_WIN32)
        if (last == '\\' || last == '/')
            return folder + name;
        return folder + "\\" + name;
#else
        if (last == '/')
            return folder + name;
        return folder + "/" + name;
#endif
    }

    std::string FileNameOf(const std::string& path)
    {
#if defined(_WIN32)
        const auto at = path.find_last_of("\\/");
#else
        const auto at = path.find_last_of('/');
#endif
        return at == std::string::npos ? path : path.substr(at + 1);
    }

    std::string FolderOf(const std::string& path)
    {
#if defined(_WIN32)
        const auto at = path.find_last_of("\\/");
#else
        const auto at = path.find_last_of('/');
#endif
        return at == std::string::npos ? std::string() : path.substr(0, at == 0 ? 1 : at);
    }

    std::string LocalDataBase()
    {
#if defined(_WIN32)
        PWSTR folder = nullptr;
        std::string base;
        if (SHGetKnownFolderPath(FOLDERID_LocalAppData, KF_FLAG_DONT_VERIFY, nullptr, &folder) == S_OK && folder != nullptr)
            base = Narrow(std::wstring(folder));
        if (folder != nullptr)
            CoTaskMemFree(folder);
        return base;
#else
        const char* xdg = std::getenv("XDG_DATA_HOME");
        if (xdg != nullptr && xdg[0] == '/')
            return xdg;
        const char* home = std::getenv("HOME");
        if (home != nullptr && home[0] == '/')
            return std::string(home) + "/.local/share";
        return std::string();
#endif
    }

    std::string TempFolder()
    {
        std::error_code code;
        const auto temp = std::filesystem::temp_directory_path(code);
        return code ? std::string() : FsString(temp);
    }

    std::string ProcessPath()
    {
#if defined(_WIN32)
        std::wstring buffer(512, L'\0');
        for (;;)
        {
            const DWORD length = GetModuleFileNameW(nullptr, buffer.data(), static_cast<DWORD>(buffer.size()));
            if (length == 0)
                return std::string();
            if (length < buffer.size())
            {
                buffer.resize(length);
                return Narrow(buffer);
            }
            if (buffer.size() >= 32768)
                return std::string();
            buffer.resize(buffer.size() * 2);
        }
#elif defined(__linux__)
        std::string buffer(4096, '\0');
        const ssize_t length = ::readlink("/proc/self/exe", buffer.data(), buffer.size());
        if (length <= 0 || static_cast<std::size_t>(length) >= buffer.size())
            return std::string();
        buffer.resize(static_cast<std::size_t>(length));
        return buffer;
#else
        return std::string();
#endif
    }

    std::string PathKeyForm(const std::string& path)
    {
#if defined(_WIN32)
        bool ok = true;
        std::wstring wide = Wide(path, ok);
        if (!ok || wide.empty())
            return path;
        CharLowerBuffW(wide.data(), static_cast<DWORD>(wide.size()));
        std::replace(wide.begin(), wide.end(), L'/', L'\\');
        return Narrow(wide);
#else
        return path;
#endif
    }

    bool IsValidUtf8(const std::string& text) noexcept
    {
        std::size_t i = 0;
        const std::size_t n = text.size();
        while (i < n)
        {
            const auto c = static_cast<unsigned char>(text[i]);
            std::size_t extra = 0;
            std::uint32_t code = 0;
            if (c < 0x80)
            {
                ++i;
                continue;
            }
            if (c >= 0xC2 && c <= 0xDF)
            {
                extra = 1;
                code = c & 0x1F;
            }
            else if (c >= 0xE0 && c <= 0xEF)
            {
                extra = 2;
                code = c & 0x0F;
            }
            else if (c >= 0xF0 && c <= 0xF4)
            {
                extra = 3;
                code = c & 0x07;
            }
            else
                return false;
            for (std::size_t k = 1; k <= extra; ++k)
            {
                if (i + k >= n)
                    return false;
                const auto next = static_cast<unsigned char>(text[i + k]);
                if ((next & 0xC0) != 0x80)
                    return false;
                code = (code << 6) | (next & 0x3F);
            }
            if ((extra == 2 && (code < 0x800 || (code >= 0xD800 && code <= 0xDFFF))) || (extra == 3 && (code < 0x10000 || code > 0x10FFFF)))
                return false;
            i += extra + 1;
        }
        return true;
    }

    std::int64_t ProcessId() noexcept
    {
#if defined(_WIN32)
        return static_cast<std::int64_t>(GetCurrentProcessId());
#else
        return static_cast<std::int64_t>(::getpid());
#endif
    }

    // ---- The uploader thread ----

    void LowerCurrentThreadPriority(std::string& error)
    {
#if defined(_WIN32)
        if (!SetThreadPriority(GetCurrentThread(), THREAD_PRIORITY_BELOW_NORMAL))
            error = LastErrorText("lowering the uploader's priority", GetLastError());
#elif defined(__linux__)
        const auto tid = static_cast<id_t>(::syscall(SYS_gettid));
        errno = 0;
        const int current = ::getpriority(PRIO_PROCESS, tid);
        if (errno == 0 && ::setpriority(PRIO_PROCESS, tid, (std::min)(current + 5, 19)) != 0)
            error = ErrnoText("lowering the uploader's priority", errno);
#else
        (void)error;
#endif
    }

    std::uintptr_t CurrentThreadToken()
    {
#if defined(_WIN32)
        HANDLE duplicate = nullptr;
        if (!DuplicateHandle(GetCurrentProcess(), GetCurrentThread(), GetCurrentProcess(), &duplicate, 0, FALSE, DUPLICATE_SAME_ACCESS))
            return 0;
        return reinterpret_cast<std::uintptr_t>(duplicate);
#elif defined(__linux__)
        return static_cast<std::uintptr_t>(::syscall(SYS_gettid));
#else
        return 0;
#endif
    }

    void ReleaseThreadToken(std::uintptr_t token) noexcept
    {
#if defined(_WIN32)
        if (token != 0)
            CloseHandle(reinterpret_cast<HANDLE>(token));
#else
        (void)token;
#endif
    }

    void RaiseThreadPriority(std::uintptr_t token) noexcept
    {
#if defined(_WIN32)
        if (token != 0)
            SetThreadPriority(reinterpret_cast<HANDLE>(token), THREAD_PRIORITY_NORMAL);
#elif defined(__linux__)
        // Lowering the nice value back needs CAP_SYS_NICE or RLIMIT_NICE; without them it fails, and the
        // thread reaches its stop check a little later.
        if (token != 0)
            ::setpriority(PRIO_PROCESS, static_cast<id_t>(token), 0);
#else
        (void)token;
#endif
    }

    void CancelThreadIo(std::uintptr_t token) noexcept
    {
#if defined(_WIN32)
        if (token != 0)
            CancelSynchronousIo(reinterpret_cast<HANDLE>(token));
#else
        (void)token;
#endif
    }
} // namespace hsm::collector::large_files
