// HSM Agent unit tests. Name-dispatched like the collector driver: `hsm_agent_tests <test_name>`
// exits 0 on pass, 77 on skip, any other non-zero on failure. The config-parser tests are portable so
// every CI lane can run them; the data-dir DACL tests (#1279) are compiled on Windows only.

#include "agent/config.hpp"

#ifdef _WIN32
#include "agent/paths.hpp"

#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#ifndef NOMINMAX
#define NOMINMAX
#endif
#include <windows.h>

#include <aclapi.h>
#include <sddl.h>
#include <vector>
#endif

#include <cstdlib>
#include <cstring>
#include <filesystem>
#include <fstream>
#include <functional>
#include <iostream>
#include <map>
#include <sstream>
#include <string>

namespace
{
    int g_failures = 0;
    bool g_skipped = false; // a test whose precondition is absent sets this; main() exits 77 (CTest SKIP_RETURN_CODE)

    void Check(bool condition, const std::string& message)
    {
        if (!condition)
        {
            std::cerr << "  FAIL: " << message << '\n';
            ++g_failures;
        }
    }

    template <typename A, typename B>
    void CheckEq(const A& actual, const B& expected, const std::string& message)
    {
        if (!(actual == expected))
        {
            std::cerr << "  FAIL: " << message << " (got '" << actual << "', expected '" << expected << "')\n";
            ++g_failures;
        }
    }

    using hsm::agent::AgentConfig;
    using hsm::agent::ParseAgentConfig;

    AgentConfig ParseOk(const std::string& json)
    {
        AgentConfig config;
        std::string error;
        if (!ParseAgentConfig(json, config, error))
        {
            std::cerr << "  FAIL: expected parse to succeed, got error: " << error << '\n';
            ++g_failures;
        }
        return config;
    }

    void ExpectReject(const std::string& json, const std::string& why)
    {
        AgentConfig config;
        std::string error;
        const bool ok = ParseAgentConfig(json, config, error);
        if (ok)
        {
            std::cerr << "  FAIL: expected rejection (" << why << ") but parse succeeded\n";
            ++g_failures;
        }
        else
        {
            Check(!error.empty(), "rejection should carry a non-empty error message");
        }
    }

    // --- Tests --------------------------------------------------------------------------------

    void MinimalConfigAppliesDefaults()
    {
        const auto config = ParseOk(R"({ "server": { "address": "https://hsm.example.com", "accessKey": "abc123" } })");
        CheckEq(config.server_address, std::string{ "https://hsm.example.com" }, "address");
        CheckEq(config.access_key, std::string{ "abc123" }, "accessKey");
        CheckEq(config.port, 44330, "default port");
        CheckEq(config.module, std::string{ "HSM Agent" }, "default module");
        CheckEq(config.product_version, std::string{ "1.0.0.0" }, "default version");
        Check(config.ComputerNameIsAuto(), "computer name defaults to auto");
        Check(config.sensors_computer && config.sensors_system && config.sensors_disk && config.sensors_network && config.sensors_module,
              "all host groups default on");
        Check(!config.sensors_process, "process sensors default off");
        CheckEq(config.collect_period_ms, 0, "default collect period (collector default)");
        Check(!config.allow_untrusted_certificate, "untrusted cert defaults off");
    }

    void FullConfigMapsEveryField()
    {
        const auto config = ParseOk(R"({
            "server": { "address": "https://h:1", "port": 9999, "accessKey": "k", "allowUntrustedCertificate": true },
            "identity": { "computerName": "BUILD01", "module": "Custom Module" },
            "sensors": { "computer": false, "system": true, "disk": false, "network": true, "module": false, "process": true },
            "periods": { "collectMs": 5000 },
            "productVersion": "2.3.4.5",
            "update": { "enabled": false, "checkPeriodHours": 48 }
        })");
        CheckEq(config.server_address, std::string{ "https://h:1" }, "address");
        CheckEq(config.port, 9999, "port");
        Check(config.allow_untrusted_certificate, "untrusted cert on");
        CheckEq(config.computer_name, std::string{ "BUILD01" }, "computer name");
        Check(!config.ComputerNameIsAuto(), "explicit computer name is not auto");
        CheckEq(config.module, std::string{ "Custom Module" }, "module");
        Check(!config.sensors_computer, "computer off");
        Check(config.sensors_system, "system on");
        Check(!config.sensors_disk, "disk off");
        Check(config.sensors_network, "network on");
        Check(!config.sensors_module, "module off");
        Check(config.sensors_process, "process on");
        CheckEq(config.collect_period_ms, 5000, "collect period");
        CheckEq(config.product_version, std::string{ "2.3.4.5" }, "version");
        Check(!config.update_enabled, "update disabled");
        CheckEq(config.update_check_period_hours, 48, "update period");
    }

    // Guards writer<->parser key parity: WriteAgentConfig must emit every field the parser reads
    // (a dropped key silently resets on the next directive-driven rewrite), and string values with
    // JSON-significant characters must survive the escape round-trip.
    void WriteReadRoundTrip()
    {
        const AgentConfig original = ParseOk(R"({
            "server": { "address": "https://h:1", "port": 9999, "accessKey": "k\"\\with\ttabs", "allowUntrustedCertificate": true },
            "identity": { "computerName": "BUILD01", "module": "Custom\\Module" },
            "sensors": { "computer": false, "system": true, "disk": false, "network": true, "module": false, "process": true },
            "periods": { "collectMs": 5000 },
            "productVersion": "2.3.4.5",
            "update": { "enabled": false, "checkPeriodHours": 48 }
        })");

        const std::string path = (std::filesystem::temp_directory_path() / "hsm_agent_roundtrip.json").string();
        std::string err;
        Check(hsm::agent::WriteAgentConfig(path, original, err), "WriteAgentConfig should succeed: " + err);

        std::ifstream in(path, std::ios::binary);
        std::stringstream ss;
        ss << in.rdbuf();
        in.close();

        const AgentConfig reparsed = ParseOk(ss.str());
        CheckEq(reparsed.product_version, original.product_version, "productVersion round-trips");
        CheckEq(reparsed.access_key, original.access_key, "accessKey with specials round-trips");
        CheckEq(reparsed.module, original.module, "module with backslash round-trips");
        CheckEq(reparsed.server_address, original.server_address, "address round-trips");
        CheckEq(reparsed.computer_name, original.computer_name, "computerName round-trips");
        CheckEq(reparsed.port, original.port, "port round-trips");
        Check(reparsed.sensors_system && reparsed.sensors_network && reparsed.sensors_process, "enabled groups round-trip");
        Check(!reparsed.sensors_computer && !reparsed.sensors_disk && !reparsed.sensors_module, "disabled groups round-trip");
        CheckEq(reparsed.collect_period_ms, original.collect_period_ms, "collect period round-trips");
        Check(reparsed.update_enabled == original.update_enabled, "update enabled round-trips");
        CheckEq(reparsed.update_check_period_hours, original.update_check_period_hours, "update period round-trips");

        std::error_code rm;
        std::filesystem::remove(path, rm);
    }

    void BlankAccessKeyIsRejected()
    {
        ExpectReject(R"({ "server": { "address": "https://h", "accessKey": "" } })", "blank key");
        ExpectReject(R"({ "server": { "address": "https://h" } })", "missing key");
    }

    void MissingAddressIsRejected()
    {
        ExpectReject(R"({ "server": { "accessKey": "k" } })", "missing address");
        ExpectReject(R"({ "server": { "address": "", "accessKey": "k" } })", "blank address");
    }

    void BadPortIsRejected()
    {
        ExpectReject(R"({ "server": { "address": "https://h", "accessKey": "k", "port": 0 } })", "port 0");
        ExpectReject(R"({ "server": { "address": "https://h", "accessKey": "k", "port": 70000 } })", "port too high");
        // Out of 32-bit range — must be rejected, not cast (casting out-of-range double to int is UB).
        ExpectReject(R"({ "server": { "address": "https://h", "accessKey": "k", "port": 1e10 } })", "port overflow");
        // Fractional value must be rejected, not silently truncated to 1.
        ExpectReject(R"({ "server": { "address": "https://h", "accessKey": "k", "port": 1.5 } })", "fractional port");
    }

    void MalformedJsonIsRejected()
    {
        ExpectReject(R"({ "server": { "address": )", "truncated");
        ExpectReject(R"(not json at all)", "garbage");
        ExpectReject(R"([])", "array root");
    }

    void WrongTypedFieldIsRejected()
    {
        ExpectReject(R"({ "server": { "address": "h", "accessKey": "k", "port": "nope" } })", "string port");
        ExpectReject(R"({ "server": { "address": 5, "accessKey": "k" } })", "numeric address");
        ExpectReject(R"({ "server": { "address": "h", "accessKey": "k" }, "sensors": { "disk": 1 } })", "numeric bool");
    }

    void UnknownFieldsAreIgnored()
    {
        const auto config = ParseOk(R"({
            "server": { "address": "h", "accessKey": "k", "futureFlag": 42 },
            "somethingNew": { "nested": [1, 2, 3] }
        })");
        CheckEq(config.server_address, std::string{ "h" }, "address survives unknown siblings");
    }

    void StringEscapesDecode()
    {
        const auto config = ParseOk(R"({ "server": { "address": "a\tbé", "accessKey": "k" } })");
        CheckEq(config.server_address, std::string{ "a\tb\xc3\xa9" }, "tab + unicode escape decode to UTF-8");
    }

    void TopCpuConfigParsesAndDefaults()
    {
        // Absent topCpu → feature off, sane defaults.
        const auto def = ParseOk(R"({ "server": { "address": "h", "accessKey": "k" } })");
        Check(!def.top_cpu_enabled, "topCpu off by default");
        CheckEq(def.top_cpu_period_ms, 60000, "default period");
        CheckEq(def.top_cpu_count, 10, "default count");

        const auto cfg = ParseOk(R"({
            "server": { "address": "h", "accessKey": "k" },
            "topCpu": { "enabled": true, "periodMs": 30000, "minPercent": 2.5, "count": 5 }
        })");
        Check(cfg.top_cpu_enabled, "enabled");
        CheckEq(cfg.top_cpu_period_ms, 30000, "periodMs");
        CheckEq(cfg.top_cpu_count, 5, "count");
        Check(cfg.top_cpu_min_percent > 2.49 && cfg.top_cpu_min_percent < 2.51, "minPercent");
    }

    void TopCpuRejectsBadConfig()
    {
        // Bad values only matter when the feature is on.
        ExpectReject(R"({ "server": { "address": "h", "accessKey": "k" }, "topCpu": { "enabled": true, "periodMs": 0 } })", "zero period");
        ExpectReject(R"({ "server": { "address": "h", "accessKey": "k" }, "topCpu": { "enabled": true, "count": 0 } })", "zero count");
        ExpectReject(R"({ "server": { "address": "h", "accessKey": "k" }, "topCpu": { "enabled": true, "minPercent": -1 } })", "negative minPercent");
        ExpectReject(R"({ "server": { "address": "h", "accessKey": "k" }, "topCpu": { "enabled": "yes" } })", "non-bool enabled");
    }

    void UpdateConfigParsesAndDefaults()
    {
        // Absent update section → enabled=true, 24 h default.
        const auto def = ParseOk(R"({ "server": { "address": "h", "accessKey": "k" } })");
        Check(def.update_enabled, "update enabled by default");
        CheckEq(def.update_check_period_hours, 24, "default check period");

        // Explicit values round-trip.
        const auto cfg = ParseOk(R"({
            "server": { "address": "h", "accessKey": "k" },
            "update": { "enabled": false, "checkPeriodHours": 12 }
        })");
        Check(!cfg.update_enabled, "disabled");
        CheckEq(cfg.update_check_period_hours, 12, "check period");
    }

    void UpdateConfigRejectsBadPeriod()
    {
        ExpectReject(R"({ "server": { "address": "h", "accessKey": "k" }, "update": { "checkPeriodHours": 0 } })", "zero period");
        ExpectReject(R"({ "server": { "address": "h", "accessKey": "k" }, "update": { "checkPeriodHours": -1 } })", "negative period");
        // 1194 h * 3600000 overflows DWORD — must be rejected.
        ExpectReject(R"({ "server": { "address": "h", "accessKey": "k" }, "update": { "checkPeriodHours": 9000 } })", "overflow period");
        ExpectReject(R"({ "server": { "address": "h", "accessKey": "k" }, "update": "on" })", "non-object update");
    }

    // Guards the sensor-group directive name<->field mapping (#1198): a typo (e.g. "disk" pointing at
    // sensors_network) would silently toggle the wrong group. SensorGroupFlag is the single source of
    // truth shared with the runtime directive handler, so testing it here covers both.
    void SensorGroupFlagMapsNames()
    {
        using hsm::agent::SensorGroupFlag;
        Check(SensorGroupFlag("computer") == &AgentConfig::sensors_computer, "computer -> sensors_computer");
        Check(SensorGroupFlag("system") == &AgentConfig::sensors_system, "system -> sensors_system");
        Check(SensorGroupFlag("disk") == &AgentConfig::sensors_disk, "disk -> sensors_disk");
        Check(SensorGroupFlag("network") == &AgentConfig::sensors_network, "network -> sensors_network");
        Check(SensorGroupFlag("module") == &AgentConfig::sensors_module, "module -> sensors_module");
        Check(SensorGroupFlag("process") == &AgentConfig::sensors_process, "process -> sensors_process");
        Check(SensorGroupFlag("bogus") == nullptr, "unknown group -> nullptr");
        Check(SensorGroupFlag("") == nullptr, "empty group -> nullptr");

        // The returned pointer-to-member reads and writes the intended field.
        AgentConfig c; // defaults: process off, the rest on
        auto process = SensorGroupFlag("process");
        Check(process != nullptr && !(c.*process), "process flag reads its default (off)");
        c.*process = true;
        Check(c.sensors_process, "writing via the pointer flips the right field");
    }

#ifdef _WIN32
    // --- Data-dir DACL (#1279) ---------------------------------------------------------------------
    //
    // Drive the real EnsureDirectories() against a scratch %ProgramData% (the env var is redirected),
    // seeded with the pre-#1279 state: a data dir that hands BUILTIN\Users read down to config.json and
    // logs\, exactly what %ProgramData% gives every subfolder.

    constexpr const char* kSystemSid = "S-1-5-18";
    constexpr const char* kAdminsSid = "S-1-5-32-544";
    constexpr const char* kUsersSid = "S-1-5-32-545";
    constexpr const char* kEveryoneSid = "S-1-1-0";
    constexpr const char* kAuthenticatedUsersSid = "S-1-5-11";

    struct AceInfo
    {
        std::string sid;
        BYTE type = 0;
        BYTE flags = 0;
        ACCESS_MASK mask = 0;
    };

    struct DaclInfo
    {
        bool read = false;
        bool is_protected = false;
        std::vector<AceInfo> aces;
    };

    std::vector<BYTE> WellKnownSid(WELL_KNOWN_SID_TYPE type)
    {
        std::vector<BYTE> sid(SECURITY_MAX_SID_SIZE);
        DWORD size = static_cast<DWORD>(sid.size());
        if (!CreateWellKnownSid(type, nullptr, sid.data(), &size))
            sid.clear();
        return sid;
    }

    DaclInfo ReadDacl(const std::filesystem::path& path)
    {
        DaclInfo info;
        PACL dacl = nullptr;
        PSECURITY_DESCRIPTOR sd = nullptr;
        if (GetNamedSecurityInfoW(path.c_str(), SE_FILE_OBJECT, DACL_SECURITY_INFORMATION, nullptr, nullptr, &dacl, nullptr, &sd) != ERROR_SUCCESS)
            return info;
        info.read = true;

        SECURITY_DESCRIPTOR_CONTROL control = 0;
        DWORD revision = 0;
        if (GetSecurityDescriptorControl(sd, &control, &revision))
            info.is_protected = (control & SE_DACL_PROTECTED) != 0;

        for (DWORD i = 0; dacl != nullptr && i < dacl->AceCount; ++i)
        {
            void* raw = nullptr;
            if (!GetAce(dacl, i, &raw))
                continue;
            const auto* header = static_cast<const ACE_HEADER*>(raw);
            AceInfo ace;
            ace.type = header->AceType;
            ace.flags = header->AceFlags;
            // ACCESS_ALLOWED_ACE and ACCESS_DENIED_ACE share the {Header, Mask, SidStart} layout.
            const auto* allowed = static_cast<const ACCESS_ALLOWED_ACE*>(raw);
            ace.mask = allowed->Mask;
            LPSTR text = nullptr;
            if (ConvertSidToStringSidA(const_cast<DWORD*>(&allowed->SidStart), &text))
            {
                ace.sid = text;
                LocalFree(text);
            }
            info.aces.push_back(ace);
        }
        LocalFree(sd);
        return info;
    }

    bool HasAceFor(const DaclInfo& info, const std::string& sid)
    {
        for (const auto& ace : info.aces)
            if (ace.sid == sid)
                return true;
        return false;
    }

    // No ACE may name Users / Everyone / Authenticated Users, and only SYSTEM + Administrators may be
    // granted anything at all (that also rules out the test runner's own user SID inherited from %TEMP%).
    void CheckOnlySystemAndAdmins(const DaclInfo& info, const std::string& what)
    {
        Check(info.read, what + ": DACL readable");
        for (const char* wide : { kUsersSid, kEveryoneSid, kAuthenticatedUsersSid })
            Check(!HasAceFor(info, wide), what + ": no ACE for wide principal " + wide);
        for (const auto& ace : info.aces)
            Check(ace.sid == kSystemSid || ace.sid == kAdminsSid, what + ": unexpected ACE for " + ace.sid);
        Check(HasAceFor(info, kSystemSid), what + ": SYSTEM has an ACE");
        Check(HasAceFor(info, kAdminsSid), what + ": Administrators has an ACE");
    }

    // A hardened directory: protected, exactly SYSTEM + Administrators, explicit full control that
    // sub-folders and files inherit — icacls shows `(OI)(CI)(F)` and no `(I)`.
    void CheckHardenedDirectory(const std::filesystem::path& path, const std::string& what)
    {
        const DaclInfo info = ReadDacl(path);
        CheckOnlySystemAndAdmins(info, what);
        Check(info.is_protected, what + ": DACL is protected (inheritance from the parent disabled)");
        CheckEq(info.aces.size(), std::size_t{ 2 }, what + ": exactly two ACEs");
        for (const auto& ace : info.aces)
        {
            Check(ace.type == ACCESS_ALLOWED_ACE_TYPE, what + ": allow ACE for " + ace.sid);
            Check(ace.mask == FILE_ALL_ACCESS, what + ": full control for " + ace.sid);
            Check((ace.flags & (OBJECT_INHERIT_ACE | CONTAINER_INHERIT_ACE)) == (OBJECT_INHERIT_ACE | CONTAINER_INHERIT_ACE),
                  what + ": (OI)(CI) for " + ace.sid);
            Check((ace.flags & INHERITED_ACE) == 0, what + ": explicit (not inherited) ACE for " + ace.sid);
        }
    }

    void SetDacl(const std::filesystem::path& path, std::vector<BYTE>& sid, DWORD permissions, ACCESS_MODE mode, SECURITY_INFORMATION extra)
    {
        PACL old_dacl = nullptr;
        PSECURITY_DESCRIPTOR sd = nullptr;
        if (mode == GRANT_ACCESS)
            GetNamedSecurityInfoW(path.c_str(), SE_FILE_OBJECT, DACL_SECURITY_INFORMATION, nullptr, nullptr, &old_dacl, nullptr, &sd);
        EXPLICIT_ACCESS_W entry = {};
        entry.grfAccessPermissions = permissions;
        entry.grfAccessMode = mode;
        entry.grfInheritance = SUB_CONTAINERS_AND_OBJECTS_INHERIT;
        entry.Trustee.TrusteeForm = TRUSTEE_IS_SID;
        entry.Trustee.ptstrName = reinterpret_cast<LPWSTR>(sid.data());
        PACL dacl = nullptr;
        if (SetEntriesInAclW(1, &entry, old_dacl, &dacl) == ERROR_SUCCESS)
        {
            SetNamedSecurityInfoW(const_cast<LPWSTR>(path.c_str()), SE_FILE_OBJECT, DACL_SECURITY_INFORMATION | extra, nullptr, nullptr, dacl, nullptr);
            LocalFree(dacl);
        }
        if (sd != nullptr)
            LocalFree(sd);
    }

    std::vector<BYTE> CurrentUserSid()
    {
        std::vector<BYTE> sid;
        HANDLE token = nullptr;
        if (!OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, &token))
            return sid;
        DWORD size = 0;
        GetTokenInformation(token, TokenUser, nullptr, 0, &size);
        std::vector<BYTE> buffer(size);
        if (size != 0 && GetTokenInformation(token, TokenUser, buffer.data(), size, &size))
        {
            const auto* user = reinterpret_cast<const TOKEN_USER*>(buffer.data());
            const DWORD length = GetLengthSid(user->User.Sid);
            sid.assign(static_cast<const BYTE*>(user->User.Sid), static_cast<const BYTE*>(user->User.Sid) + length);
        }
        CloseHandle(token);
        return sid;
    }

    // True when the token can use BUILTIN\Administrators (elevated admin or SYSTEM). An unelevated UAC
    // token carries the group deny-only, so the hardened DACL locks it out of the files.
    bool IsElevatedAdmin()
    {
        std::vector<BYTE> admins = WellKnownSid(WinBuiltinAdministratorsSid);
        BOOL member = FALSE;
        return !admins.empty() && CheckTokenMembership(nullptr, admins.data(), &member) && member;
    }

    void WriteFileBytes(const std::filesystem::path& path, const std::string& text)
    {
        std::ofstream out(path, std::ios::binary | std::ios::trunc);
        out << text;
    }

    std::string ReadFileBytes(const std::filesystem::path& path)
    {
        std::ifstream in(path, std::ios::binary);
        std::stringstream ss;
        ss << in.rdbuf();
        return ss.str();
    }

    // Scratch %ProgramData% with a pre-#1279 agent data dir in it; restores the env var and removes
    // the tree on exit (re-granting the runner access first, since an unelevated runner is locked out).
    class LegacyDataDir
    {
    public:
        explicit LegacyDataDir(const char* tag, const std::string& config_text)
        {
            root_ = std::filesystem::temp_directory_path() / (std::string("hsm_agent_acl_") + tag + "_" + std::to_string(GetCurrentProcessId()));
            Remove();
            std::filesystem::create_directories(logs());

            wchar_t* old = nullptr;
            std::size_t old_length = 0;
            if (_wdupenv_s(&old, &old_length, L"ProgramData") == 0 && old != nullptr)
            {
                had_old_ = true;
                old_program_data_ = old;
            }
            free(old);
            _wputenv_s(L"ProgramData", root_.c_str());

            // What %ProgramData% hands every subfolder: an inheritable BUILTIN\Users read + execute ACE.
            std::vector<BYTE> users = WellKnownSid(WinBuiltinUsersSid);
            SetDacl(data(), users, FILE_GENERIC_READ | FILE_GENERIC_EXECUTE, GRANT_ACCESS, UNPROTECTED_DACL_SECURITY_INFORMATION);
            WriteFileBytes(config(), config_text);
            WriteFileBytes(logs() / "hsm-agent.log", "2026-01-01 INFO pre-#1279 log line\n");

            Check(HasAceFor(ReadDacl(config()), kUsersSid), "setup: legacy config.json inherits a BUILTIN\\Users ACE (else the test is vacuous)");
            Check(HasAceFor(ReadDacl(logs() / "hsm-agent.log"), kUsersSid), "setup: legacy log inherits a BUILTIN\\Users ACE");
        }

        ~LegacyDataDir()
        {
            _wputenv_s(L"ProgramData", had_old_ ? old_program_data_.c_str() : L"");
            Remove();
        }

        LegacyDataDir(const LegacyDataDir&) = delete;
        LegacyDataDir& operator=(const LegacyDataDir&) = delete;

        std::filesystem::path data() const { return root_ / "HSM Agent"; }
        std::filesystem::path logs() const { return data() / "logs"; }
        std::filesystem::path config() const { return data() / "config.json"; }

    private:
        void Remove()
        {
            std::error_code ec;
            if (!std::filesystem::exists(root_, ec))
                return;
            // The runner owns everything it created, so it keeps WRITE_DAC even when the hardened DACL
            // denies it everything else: re-grant itself full control, then delete.
            std::vector<BYTE> me = CurrentUserSid();
            if (!me.empty() && std::filesystem::exists(data(), ec))
            {
                SetDacl(data(), me, FILE_ALL_ACCESS, SET_ACCESS, PROTECTED_DACL_SECURITY_INFORMATION);
                SetDacl(logs(), me, FILE_ALL_ACCESS, SET_ACCESS, UNPROTECTED_DACL_SECURITY_INFORMATION);
            }
            std::filesystem::remove_all(root_, ec);
            if (ec)
                std::cerr << "  note: could not remove scratch dir " << root_.string() << ": " << ec.message() << '\n';
        }

        std::filesystem::path root_;
        bool had_old_ = false;
        std::wstring old_program_data_;
    };

    constexpr const char* kLegacyConfig = R"({ "server": { "address": "https://hsm.example.com", "accessKey": "legacy-key-1279" }, "sensors": { "disk": true } })";

    // The effective DACLs after EnsureDirectories(): data dir + logs\ hardened, the files that inherited
    // the wide ACL tightened in place, and a second call (the next service start) is a clean no-op.
    void DirAclIsProtectedSystemAndAdminsOnly()
    {
        LegacyDataDir dir("protected", kLegacyConfig);

        for (int start = 1; start <= 2; ++start)
        {
            const std::string run = "start #" + std::to_string(start) + ": ";
            std::vector<std::string> acl_errors;
            Check(hsm::agent::EnsureDirectories(&acl_errors), run + "EnsureDirectories reports the data dir present");
            for (const auto& error : acl_errors)
                Check(false, run + "unexpected ACL error: " + error);

            Check(hsm::agent::ProgramDataAgentDir() == dir.data().wstring(), run + "agent paths follow the redirected %ProgramData%");
            CheckHardenedDirectory(dir.data(), run + "data dir");
            CheckHardenedDirectory(dir.logs(), run + "logs dir");
            CheckOnlySystemAndAdmins(ReadDacl(dir.config()), run + "existing config.json");
            CheckOnlySystemAndAdmins(ReadDacl(dir.logs() / "hsm-agent.log"), run + "existing log file");
        }
    }

    // The agent itself (SYSTEM / an elevated admin) keeps working inside the hardened dir: an existing
    // config survives the upgrade intact, and the directive rewrite (WriteAgentConfig: write
    // config.json.tmp + rename over config.json) succeeds and lands with the same narrow inherited DACL.
    void DirAclAdminReadsAndRewritesConfig()
    {
        if (!IsElevatedAdmin())
        {
            std::cout << "skipped: needs an elevated (admin) process — the hardened DACL admits only SYSTEM + Administrators\n";
            g_skipped = true;
            return;
        }

        LegacyDataDir dir("rewrite", kLegacyConfig);
        std::vector<std::string> acl_errors;
        Check(hsm::agent::EnsureDirectories(&acl_errors), "EnsureDirectories reports the data dir present");
        for (const auto& error : acl_errors)
            Check(false, "unexpected ACL error: " + error);

        const std::string before = ReadFileBytes(dir.config());
        CheckEq(before, std::string{ kLegacyConfig }, "upgrade keeps config.json byte-identical");
        AgentConfig config = ParseOk(before);
        CheckEq(config.access_key, std::string{ "legacy-key-1279" }, "access key still readable by the agent");

        config.sensors_disk = false; // what a sensor-group directive does
        std::string error;
        Check(hsm::agent::WriteAgentConfig(dir.config().string(), config, error), "directive rewrite succeeds: " + error);
        const AgentConfig reparsed = ParseOk(ReadFileBytes(dir.config()));
        Check(!reparsed.sensors_disk, "rewritten flag persisted");
        CheckEq(reparsed.access_key, std::string{ "legacy-key-1279" }, "rewrite keeps the access key");
        std::error_code ec;
        Check(!std::filesystem::exists(dir.data() / "config.json.tmp", ec), "no config.json.tmp left behind");

        const DaclInfo rewritten = ReadDacl(dir.config());
        CheckOnlySystemAndAdmins(rewritten, "rewritten config.json");
        for (const auto& ace : rewritten.aces)
            Check((ace.flags & INHERITED_ACE) != 0, "rewritten config.json ACE for " + ace.sid + " is inherited from the hardened dir");
    }
#endif // _WIN32

    const std::map<std::string, std::function<void()>>& Tests()
    {
        static const std::map<std::string, std::function<void()>> tests = {
            { "agent_config_minimal_applies_defaults", MinimalConfigAppliesDefaults },
            { "agent_config_full_maps_every_field", FullConfigMapsEveryField },
            { "agent_config_rejects_blank_access_key", BlankAccessKeyIsRejected },
            { "agent_config_rejects_missing_address", MissingAddressIsRejected },
            { "agent_config_rejects_bad_port", BadPortIsRejected },
            { "agent_config_rejects_malformed_json", MalformedJsonIsRejected },
            { "agent_config_rejects_wrong_typed_field", WrongTypedFieldIsRejected },
            { "agent_config_ignores_unknown_fields", UnknownFieldsAreIgnored },
            { "agent_config_decodes_string_escapes", StringEscapesDecode },
            { "agent_config_write_read_roundtrip", WriteReadRoundTrip },
            { "agent_topcpu_config_parses_and_defaults", TopCpuConfigParsesAndDefaults },
            { "agent_topcpu_rejects_bad_config", TopCpuRejectsBadConfig },
            { "agent_update_config_parses_and_defaults", UpdateConfigParsesAndDefaults },
            { "agent_update_config_rejects_bad_period", UpdateConfigRejectsBadPeriod },
            { "agent_sensor_group_flag_maps_names", SensorGroupFlagMapsNames },
#ifdef _WIN32
            { "agent_dir_acl_is_protected_system_and_admins_only", DirAclIsProtectedSystemAndAdminsOnly },
            { "agent_dir_acl_admin_reads_and_rewrites_config", DirAclAdminReadsAndRewritesConfig },
#endif
        };
        return tests;
    }
} // namespace

int main(int argc, char** argv)
{
    if (argc < 2)
    {
        std::cerr << "usage: hsm_agent_tests <test_name>\n";
        for (const auto& test : Tests())
            std::cerr << "  " << test.first << '\n';
        return 2;
    }

    const std::string name = argv[1];
    const auto& tests = Tests();
    const auto it = tests.find(name);
    if (it == tests.end())
    {
        std::cerr << "unknown test: " << name << '\n';
        return 2;
    }

    it->second();
    if (g_failures != 0)
    {
        std::cerr << name << ": " << g_failures << " check(s) failed\n";
        return 1;
    }
    if (g_skipped)
    {
        std::cout << name << ": skipped\n";
        return 77;
    }
    std::cout << name << ": ok\n";
    return 0;
}
