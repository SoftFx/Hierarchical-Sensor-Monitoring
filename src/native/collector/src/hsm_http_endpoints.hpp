#pragma once

#include <cctype>
#include <string>

#include "hsm_collector/hsm_collector.h"

// Native mirror of the .NET collector's Endpoints + per-type routing
// (src/collector/HSMDataCollector/Client/HttpsClient/Endpoints.cs,
//  RequestHandlers/DataHandlers.cs, RequestHandlers/CommandHandlers.cs).
//
// Pure string logic, no libcurl — compiled into the always-on core so the route table and
// the scheme-defaulting rules are unit-tested in the default /WX lane, not only the HTTP build.
//
// Faithful to the .NET behaviour for the inputs the collector actually feeds it (a bare host or
// a "scheme://host" URL). The .NET side runs ServerUrl through UriBuilder{Port, Path="api/sensors"};
// we reproduce the observable result — "<scheme>://<host>:<port>/api/sensors" with the scheme
// defaulted to https unless an explicit http scheme is paired with AllowPlaintextTransport.
// UriBuilder corner cases the collector never produces (userinfo, query, embedded path) are out
// of scope; the host is taken verbatim after the scheme is stripped.
namespace hsm::http
{
    struct Endpoints
    {
        std::string connection_address; // "<scheme>://<host>:<port>/api/sensors"

        // Command routes.
        std::string AddOrUpdateSensor() const { return connection_address + "/addOrUpdate"; }
        std::string CommandsList() const { return connection_address + "/commands"; }

        // Single-value routes (one per sensor kind), matching DataHandlers.GetUri.
        std::string Bool() const { return connection_address + "/bool"; }
        std::string Integer() const { return connection_address + "/int"; }
        std::string Double() const { return connection_address + "/double"; }
        std::string String() const { return connection_address + "/string"; }
        std::string Timespan() const { return connection_address + "/timespan"; }
        std::string Version() const { return connection_address + "/version"; }
        std::string Rate() const { return connection_address + "/rate"; }
        std::string Enum() const { return connection_address + "/enum"; }
        std::string DoubleBar() const { return connection_address + "/doubleBar"; }
        std::string IntBar() const { return connection_address + "/intBar"; }

        // Batch / file routes.
        std::string List() const { return connection_address + "/list"; }
        std::string File() const { return connection_address + "/file"; }

        std::string TestConnection() const { return connection_address + "/testConnection"; }
    };

    namespace detail
    {
        // Strip a leading "scheme://" and return (scheme, remainder). scheme is empty when absent.
        inline void SplitScheme(const std::string& url, std::string& scheme, std::string& rest)
        {
            const auto sep = url.find("://");
            if (sep == std::string::npos)
            {
                scheme.clear();
                rest = url;
                return;
            }

            scheme = url.substr(0, sep);
            rest = url.substr(sep + 3);
        }

        // Keep only the host: drop any path, query, or :port that an explicit URL might carry
        // (the collector passes Port separately, so an embedded port is ignored just as UriBuilder
        // overwrites it with the explicit Port). NOTE: a bracketed IPv6 literal ("[::1]") would be
        // truncated at its first ':' — out of scope because the collector only feeds a hostname or
        // "scheme://hostname", never an IPv6 literal; revisit if ServerAddress becomes IPv6-capable.
        inline std::string HostOnly(const std::string& authority)
        {
            std::string host = authority;
            const auto slash = host.find('/');
            if (slash != std::string::npos)
                host = host.substr(0, slash);

            const auto colon = host.find(':');
            if (colon != std::string::npos)
                host = host.substr(0, colon);

            return host;
        }

        inline bool EqualsIgnoreCase(const std::string& a, const char* b)
        {
            size_t i = 0;
            for (; i < a.size() && b[i] != '\0'; ++i)
            {
                const char ca = static_cast<char>(std::tolower(static_cast<unsigned char>(a[i])));
                const char cb = static_cast<char>(std::tolower(static_cast<unsigned char>(b[i])));
                if (ca != cb)
                    return false;
            }
            return i == a.size() && b[i] == '\0';
        }
    } // namespace detail

    // Mirror of Endpoints(CollectorOptions): scheme defaulting + the /api/sensors base path.
    inline Endpoints MakeEndpoints(const std::string& server_url, int32_t port, bool allow_plaintext)
    {
        std::string scheme;
        std::string rest;
        detail::SplitScheme(server_url, scheme, rest);

        const bool has_explicit_scheme = !scheme.empty();
        const std::string host = detail::HostOnly(rest);

        std::string effective_scheme = "https";
        if (has_explicit_scheme)
        {
            if (detail::EqualsIgnoreCase(scheme, "http") && allow_plaintext)
                effective_scheme = "http";
            else if (!detail::EqualsIgnoreCase(scheme, "http"))
                effective_scheme = scheme; // preserve an explicit https (or any non-http) scheme
        }

        Endpoints endpoints;
        endpoints.connection_address =
            effective_scheme + "://" + host + ":" + std::to_string(port) + "/api/sensors";
        return endpoints;
    }

    // DataHandlers.GetUri: a single value goes to its kind-specific route; a batch goes to /list.
    inline std::string RouteForSensorValue(const Endpoints& endpoints, hsm_sensor_type_t type, bool is_batch)
    {
        if (is_batch)
            return endpoints.List();

        switch (type)
        {
        case HSM_SENSOR_TYPE_BOOLEAN:
            return endpoints.Bool();
        case HSM_SENSOR_TYPE_INT:
            return endpoints.Integer();
        case HSM_SENSOR_TYPE_DOUBLE:
            return endpoints.Double();
        case HSM_SENSOR_TYPE_STRING:
            return endpoints.String();
        case HSM_SENSOR_TYPE_INT_BAR:
            return endpoints.IntBar();
        case HSM_SENSOR_TYPE_DOUBLE_BAR:
            return endpoints.DoubleBar();
        case HSM_SENSOR_TYPE_FILE:
            return endpoints.File();
        case HSM_SENSOR_TYPE_RATE:
            return endpoints.Rate();
        case HSM_SENSOR_TYPE_ENUM:
            return endpoints.Enum();
        default:
            return {}; // Unsupported kind — the .NET side throws; the caller treats empty as "no route".
        }
    }

    // CommandHandler.GetUri: a batch of commands → /commands, a single AddOrUpdate → /addOrUpdate.
    inline std::string RouteForCommand(const Endpoints& endpoints, bool is_batch)
    {
        return is_batch ? endpoints.CommandsList() : endpoints.AddOrUpdateSensor();
    }
} // namespace hsm::http
