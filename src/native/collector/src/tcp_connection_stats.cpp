#include "tcp_connection_stats.hpp"

namespace hsm::collector
{
    // --- Portable accumulator (no OS dependency; unit-tested directly) ---------------------------
    //
    // dwAttemptFails is the cumulative count of failed connection attempts — the same quantity the
    // managed/PDH "TCPv4|TCPv6 / Connection Failures" counter reports. We keep a SEPARATE baseline per
    // address family and sum the per-family deltas, so an intermittently-unreadable family just
    // contributes 0 for that tick (and resumes cleanly from its own baseline) instead of poisoning a
    // shared baseline.

    void TcpFailureRateAccumulator::AccumulateFamily(FamilyBaseline& family, const FamilyReading& reading,
                                                     bool& any_readable, std::uint64_t& total_delta)
    {
        if (!reading.readable)
            return; // unreadable this tick: contribute 0, keep the baseline so it resumes cleanly

        any_readable = true;
        const std::uint64_t current = reading.attempt_fails;

        if (!family.have_baseline)
        {
            family.prev = current;
            family.have_baseline = true;
            return; // first reading of this family seeds its baseline, contributes nothing
        }

        if (current >= family.prev)
            total_delta += current - family.prev;
        // else: counter reset/wrap for this family — add nothing, just re-baseline below.
        family.prev = current;
    }

    std::optional<double> TcpFailureRateAccumulator::Accumulate(const FamilyReading& v4, const FamilyReading& v6)
    {
        bool any_readable = false;
        std::uint64_t total_delta = 0;

        AccumulateFamily(v4_, v4, any_readable, total_delta);
        AccumulateFamily(v6_, v6, any_readable, total_delta);

        if (!any_readable)
            return std::nullopt; // neither family readable this tick — skip, keep baselines
        if (total_delta == 0)
            return std::nullopt; // quiet interval (or baseline seeding) — nothing pushed, rate stays 0

        return static_cast<double>(total_delta);
    }
} // namespace hsm::collector

#ifdef _WIN32

// winsock2.h must precede windows.h so the WinSock2 types are available when iphlpapi.h pulls in the
// network-stack structures (same include ordering as network_speed.cpp).
#include <winsock2.h>
#include <ws2tcpip.h>

#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <windows.h>

#include <iphlpapi.h>

namespace hsm::collector
{
    std::optional<double> WindowsTcpFailureSampler::Sample()
    {
        // Read each family's cumulative dwAttemptFails; an unreadable family is reported as not-readable
        // so the accumulator keeps its baseline. All baseline/delta/wrap/quiet math lives in the
        // portable TcpFailureRateAccumulator (unit-tested), keeping this a thin OS wrapper.
        const auto read_family = [](int address_family) -> TcpFailureRateAccumulator::FamilyReading {
            MIB_TCPSTATS stats{};
            if (GetTcpStatisticsEx(&stats, address_family) != NO_ERROR)
                return { false, 0 };
            return { true, stats.dwAttemptFails };
        };

        return accumulator_.Accumulate(read_family(AF_INET), read_family(AF_INET6));
    }
} // namespace hsm::collector

#endif // _WIN32
