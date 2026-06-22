// Crypto value-source plugin example (#1164). Demonstrates the metric-source seam as a PLUGIN point:
// a custom IMetricSource fetches the BTCUSDT spot price from a public API (Binance) and the collector
// posts it as a live Double sensor to a real HSM server. No collector change is needed for a new data
// source — just an IMetricSource + a factory that maps a sensor path to it.
//
// Built only with HSM_COLLECTOR_HTTP=ON (links libcurl, used both for the plugin fetch and the
// collector transport).
//
// Usage: hsm_crypto_monitor [server_address] [port] [access_key] [seconds]
//   defaults: https://localhost 44330 demo-key 30

#include <hsm_collector/hsm_collector.hpp>

#include <chrono>
#include <cstdlib>
#include <iostream>
#include <memory>
#include <optional>
#include <string>
#include <thread>

#include <curl/curl.h>

namespace
{

    size_t AppendBody(char* data, size_t size, size_t nmemb, void* user)
    {
        static_cast<std::string*>(user)->append(data, size * nmemb);
        return size * nmemb;
    }

    bool HttpGet(const std::string& url, std::string& out_body)
    {
        CURL* curl = curl_easy_init();
        if (curl == nullptr)
            return false;

        out_body.clear();
        curl_easy_setopt(curl, CURLOPT_URL, url.c_str());
        curl_easy_setopt(curl, CURLOPT_WRITEFUNCTION, &AppendBody);
        curl_easy_setopt(curl, CURLOPT_WRITEDATA, &out_body);
        curl_easy_setopt(curl, CURLOPT_TIMEOUT, 5L);
        curl_easy_setopt(curl, CURLOPT_FOLLOWLOCATION, 1L);

        const CURLcode rc = curl_easy_perform(curl);
        long status = 0;
        curl_easy_getinfo(curl, CURLINFO_RESPONSE_CODE, &status);
        curl_easy_cleanup(curl);

        return rc == CURLE_OK && status >= 200 && status < 300;
    }

    // The plugin: each Read() fetches the live BTCUSDT price. A transient failure (network / parse)
    // returns std::nullopt, which the collector treats as "no value this tick" (it skips, no error).
    class BinanceBtcUsdSource : public hsm::collector::IMetricSource
    {
    public:
        std::optional<double> Read() override
        {
            std::string body;
            if (!HttpGet("https://api.binance.com/api/v3/ticker/price?symbol=BTCUSDT", body))
                return std::nullopt;

            // {"symbol":"BTCUSDT","price":"64182.85000000"}
            const std::string key = "\"price\":\"";
            const auto start = body.find(key);
            if (start == std::string::npos)
                return std::nullopt;

            const auto from = start + key.size();
            const auto end = body.find('"', from);
            if (end == std::string::npos)
                return std::nullopt;

            try
            {
                return std::stod(body.substr(from, end - from));
            }
            catch (...)
            {
                return std::nullopt;
            }
        }
    };

} // namespace

int main(int argc, char** argv)
{
    namespace hc = hsm::collector;

    const std::string server = argc > 1 ? argv[1] : "https://localhost";
    const int port = argc > 2 ? std::atoi(argv[2]) : 44330;
    const std::string access_key = argc > 3 ? argv[3] : "demo-key";
    const int seconds = argc > 4 ? std::atoi(argv[4]) : 30;

    curl_global_init(CURL_GLOBAL_DEFAULT);

    try
    {
        hc::CollectorOptions options;
        options.access_key = access_key;
        options.server_address = server;
        options.port = port;
        options.module = "native-crypto-monitor";
        options.computer_name = "demo-host";
        options.allow_untrusted_server_certificate = true;
        options.package_collect_period_ms = 1000;

        hc::Collector collector(options);
        collector.SetLogger([](hc::LogLevel level, const std::string& message) {
            std::cerr << "[hsm] (" << static_cast<int>(level) << ") " << message << '\n';
        });

        // Install the value-source plugin: a factory that maps the crypto sensor path to the Binance
        // reader. Any other path returns nullptr (no source).
        collector.SetMetricSourceFactory([](const std::string& sensor_path) -> std::unique_ptr<hc::IMetricSource> {
            if (sensor_path.find("BTCUSD") != std::string::npos)
                return std::make_unique<BinanceBtcUsdSource>();
            return nullptr;
        });

        collector.UseHttpTransport();

        // A Double sensor whose value the plugin supplies every 2 s.
        auto btc = collector.CreateMetricSensor("Crypto/BTCUSD", std::chrono::seconds(2));

        std::cout << "Streaming BTCUSDT -> " << server << ':' << port << " for " << seconds << "s...\n";
        collector.Start();
        std::this_thread::sleep_for(std::chrono::seconds(seconds));
        collector.Stop();

        std::cout << "done. registrations=" << collector.RegistrationCount() << '\n';
        curl_global_cleanup();
        return 0;
    }
    catch (const std::exception& ex)
    {
        std::cerr << "error: " << ex.what() << '\n';
        curl_global_cleanup();
        return 1;
    }
}
