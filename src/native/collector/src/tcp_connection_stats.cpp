#include "tcp_connection_stats.hpp"

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
        // dwAttemptFails is the cumulative count of failed connection attempts — the same quantity the
        // managed/PDH "TCPv4|TCPv6 / Connection Failures" counter reports. Sum IPv4 + IPv6.
        std::uint64_t current = 0;
        bool any = false;

        MIB_TCPSTATS v4{};
        if (GetTcpStatisticsEx(&v4, AF_INET) == NO_ERROR)
        {
            current += v4.dwAttemptFails;
            any = true;
        }

        MIB_TCPSTATS v6{};
        if (GetTcpStatisticsEx(&v6, AF_INET6) == NO_ERROR)
        {
            current += v6.dwAttemptFails;
            any = true;
        }

        if (!any)
            return std::nullopt; // both reads failed — skip this tick, keep the baseline

        if (!have_baseline_)
        {
            prev_ = current;
            have_baseline_ = true;
            return std::nullopt; // first call seeds the baseline, posts nothing
        }

        if (current < prev_)
        {
            prev_ = current; // counter reset/wrap — re-baseline, skip
            return std::nullopt;
        }

        const std::uint64_t delta = current - prev_;
        prev_ = current;

        if (delta == 0)
            return std::nullopt; // quiet interval — nothing pushed, the rate window stays 0

        return static_cast<double>(delta);
    }
} // namespace hsm::collector

#endif // _WIN32
