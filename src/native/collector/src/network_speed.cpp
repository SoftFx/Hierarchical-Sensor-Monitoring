#include "network_speed.hpp"

#ifdef _WIN32

// winsock2.h must come before windows.h so the WinSock2 types are available when
// iphlpapi.h / netioapi.h pull in the network-stack structures.
#include <winsock2.h>
#include <ws2tcpip.h>

#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <windows.h>

#include <iphlpapi.h>
#include <netioapi.h>

namespace hsm::collector
{
    namespace
    {
        std::string AliasToUtf8(const wchar_t* wide)
        {
            if (wide == nullptr || wide[0] == L'\0')
                return std::string{};
            const int needed = WideCharToMultiByte(CP_UTF8, 0, wide, -1, nullptr, 0, nullptr, nullptr);
            if (needed <= 1)
                return std::string{};
            std::string out(static_cast<std::size_t>(needed - 1), '\0');
            WideCharToMultiByte(CP_UTF8, 0, wide, -1, out.data(), needed, nullptr, nullptr);
            return out;
        }
    } // namespace

    std::map<std::string, InterfaceSpeed> WindowsNetworkSampler::Sample()
    {
        // Monotonic elapsed time — GetTickCount64 is immune to NTP/DST/wall-clock adjustments.
        const std::uint64_t wall_ms = GetTickCount64();

        MIB_IF_TABLE2* table = nullptr;
        if (GetIfTable2(&table) != NO_ERROR || table == nullptr)
        {
            prev_wall_ms_ = wall_ms;
            have_baseline_ = true;
            return {};
        }

        // Build current snapshot: alias -> (InOctets, OutOctets) for Up, non-loopback interfaces.
        std::map<std::string, PrevEntry> cur;
        for (ULONG i = 0; i < table->NumEntries; ++i)
        {
            const MIB_IF_ROW2& row = table->Table[i];
            if (row.OperStatus != IfOperStatusUp)
                continue;
            if (row.Type == IF_TYPE_SOFTWARE_LOOPBACK)
                continue;

            const std::string alias = AliasToUtf8(row.Alias);
            if (alias.empty())
                continue;

            PrevEntry entry;
            entry.rx_octets = row.InOctets;
            entry.tx_octets = row.OutOctets;
            cur[alias] = entry;
        }
        FreeMibTable(table);

        std::map<std::string, InterfaceSpeed> result;

        const std::uint64_t wall_delta_ms = wall_ms > prev_wall_ms_ ? wall_ms - prev_wall_ms_ : 0;
        if (have_baseline_ && wall_delta_ms > 0)
        {
            const double elapsed_sec = static_cast<double>(wall_delta_ms) / 1000.0;
            const double bytes_to_mb = 1.0 / (1024.0 * 1024.0);

            for (const auto& [alias, entry] : cur)
            {
                const auto prev_it = prev_.find(alias);
                if (prev_it == prev_.end())
                    continue; // first sighting — will appear next tick

                // Negative delta = counter reset or interface bounce; skip this interval.
                if (entry.rx_octets < prev_it->second.rx_octets ||
                    entry.tx_octets < prev_it->second.tx_octets)
                    continue;

                const double rx_mb =
                    static_cast<double>(entry.rx_octets - prev_it->second.rx_octets) *
                    bytes_to_mb / elapsed_sec;
                const double tx_mb =
                    static_cast<double>(entry.tx_octets - prev_it->second.tx_octets) *
                    bytes_to_mb / elapsed_sec;

                result[alias] = { alias, rx_mb, tx_mb };
            }
        }

        prev_ = std::move(cur);
        prev_wall_ms_ = wall_ms;
        have_baseline_ = true;
        return result; // empty on the first call (baseline only)
    }

} // namespace hsm::collector

#endif // _WIN32
