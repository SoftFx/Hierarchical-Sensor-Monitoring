#include "pch.h"

#include "DataCollectorImpl.h"

namespace hsm_wrapper
{
	namespace
	{
		hsm::collector::CollectorOptions MakeOptions(const std::string& key, const std::string& address, int port, const std::string& module)
		{
			hsm::collector::CollectorOptions options;
			options.access_key = key;
			options.server_address = address;
			options.port = port;
			options.module = module;
			return options;
		}

		hsm::collector::SensorOptions DescriptionOptions(const std::string& description)
		{
			hsm::collector::SensorOptions options;
			options.description = description;
			return options;
		}

		hsm::collector::SensorStatus ToNativeStatus(HSMSensorStatus status)
		{
			return static_cast<hsm::collector::SensorStatus>(status);
		}
	}

	DataCollectorImpl::DataCollectorImpl(const std::string& product_key, const std::string& address, int port, const std::string& module)
		: collector_(MakeOptions(product_key, address, port, module))
	{
	}

	void DataCollectorImpl::Initialize(const std::string& /*config_path*/, bool /*write_debug*/)
	{
		// The managed Initialize loaded an optional config file and toggled debug logging; the native
		// collector is configured entirely through CollectorOptions, so nothing is needed here.
	}

	void DataCollectorImpl::Start()
	{
#ifdef _WIN32
		collector_.InstallWindowsMetricSources();
#endif
		collector_.UseHttpTransport();
		collector_.Start();
	}

	void DataCollectorImpl::StartAsync()
	{
#ifdef _WIN32
		collector_.InstallWindowsMetricSources();
#endif
		collector_.UseHttpTransport();
		start_future_ = collector_.StartAsync();
	}

	void DataCollectorImpl::Stop()
	{
		collector_.Stop();
	}

	void DataCollectorImpl::StopAsync()
	{
		stop_future_ = collector_.StopAsync();
	}

	// The boolean sub-flags select individual sub-sensors in the managed API; the native group helpers
	// register the standard catalog for the group, so the flags are accepted for source compatibility
	// but the whole group is registered (the aggregator passes the defaults — everything on).
	void DataCollectorImpl::InitializeSystemMonitoring(bool, bool, bool)
	{
		collector_.AddSystemMonitoringSensors();
	}

	void DataCollectorImpl::InitializeDiskMonitoring(const std::string&, bool, bool, bool, bool, bool)
	{
		collector_.AddDiskMonitoringSensors();
	}

	void DataCollectorImpl::InitializeAllDisksMonitoring(bool, bool, bool, bool, bool)
	{
		collector_.AddDiskMonitoringSensors();
	}

	void DataCollectorImpl::InitializeProcessMonitoring(bool, bool, bool, bool)
	{
		collector_.AddProcessMonitoringSensors();
	}

	void DataCollectorImpl::InitializeOsMonitoring(bool, bool, bool)
	{
		collector_.AddWindowsInfoMonitoringSensors();
	}

	void DataCollectorImpl::InitializeOsLogsMonitoring(bool, bool)
	{
		// OS logs are part of the native "Windows OS info" group; idempotent if already added.
		collector_.AddWindowsInfoMonitoringSensors();
	}

	void DataCollectorImpl::InitializeProductVersion(const std::string& version)
	{
		hsm::collector::DefaultSensorParams params;
		params.product_version = version;
		collector_.AddDefaultSensor(hsm::collector::DefaultSensor::ProductVersion, params);
	}

	void DataCollectorImpl::InitializeNetworkMonitoring(bool, bool, bool)
	{
		collector_.AddAllNetworkSensors();
	}

	void DataCollectorImpl::InitializeQueueDiagnostic(bool, bool, bool, bool)
	{
		collector_.AddAllQueueDiagnosticSensors();
	}

	void DataCollectorImpl::InitializeCollectorMonitoring(bool, bool, bool)
	{
		collector_.AddCollectorMonitoringSensors();
	}

	void DataCollectorImpl::AddServiceStateMonitoring(const std::string& service_name)
	{
		collector_.EnableServiceStatusMonitoring(service_name, std::chrono::seconds(5));
	}

	void DataCollectorImpl::SendFileAsync(const std::string& sensor_path, const std::string& file_path, HSMSensorStatus status, const std::string& description)
	{
		// Resolve (create-or-reuse) the file sensor under the lock, then release it before the file
		// read + send: SendFile reads from disk synchronously, and holding the lock across it would
		// serialize concurrent sends to *different* sensors. unordered_map element pointers stay valid
		// across inserts/rehashes, and the native FileSensor is itself thread-safe.
		hsm::collector::FileSensor* sensor = nullptr;
		{
			std::lock_guard<std::mutex> guard(file_sensors_mutex_);
			auto it = file_sensors_.find(sensor_path);
			if (it == file_sensors_.end())
				it = file_sensors_.emplace(sensor_path, collector_.CreateFileSensor(sensor_path, "file", "txt")).first;
			sensor = &it->second;
		}
		sensor->SendFile(file_path, ToNativeStatus(status), description);
	}

	BoolSensor DataCollectorImpl::CreateBoolSensor(const std::string& path, const std::string& description)
	{
		return BoolSensor(std::make_shared<HSMSensorImpl<bool>>(collector_.CreateBoolSensor(path, DescriptionOptions(description))));
	}

	BoolSensor DataCollectorImpl::CreateBoolSensor(const std::string& path, const HSMInstantSensorOptions& options)
	{
		auto native = collector_.CreateBoolSensor(path, ToNativeInstantOptions(options));
		for (const auto& alert : options.alerts)
			AttachWrapperAlert(collector_, native, alert.Impl()->Data());
		return BoolSensor(std::make_shared<HSMSensorImpl<bool>>(std::move(native)));
	}

	IntSensor DataCollectorImpl::CreateIntSensor(const std::string& path, const std::string& description)
	{
		return IntSensor(std::make_shared<HSMSensorImpl<int>>(collector_.CreateIntSensor(path, DescriptionOptions(description))));
	}

	IntSensor DataCollectorImpl::CreateIntSensor(const std::string& path, const HSMInstantSensorOptions& options)
	{
		auto native = collector_.CreateIntSensor(path, ToNativeInstantOptions(options));
		for (const auto& alert : options.alerts)
			AttachWrapperAlert(collector_, native, alert.Impl()->Data());
		return IntSensor(std::make_shared<HSMSensorImpl<int>>(std::move(native)));
	}

	DoubleSensor DataCollectorImpl::CreateDoubleSensor(const std::string& path, const std::string& description)
	{
		return DoubleSensor(std::make_shared<HSMSensorImpl<double>>(collector_.CreateDoubleSensor(path, DescriptionOptions(description))));
	}

	DoubleSensor DataCollectorImpl::CreateDoubleSensor(const std::string& path, const HSMInstantSensorOptions& options)
	{
		auto native = collector_.CreateDoubleSensor(path, ToNativeInstantOptions(options));
		for (const auto& alert : options.alerts)
			AttachWrapperAlert(collector_, native, alert.Impl()->Data());
		return DoubleSensor(std::make_shared<HSMSensorImpl<double>>(std::move(native)));
	}

	StringSensor DataCollectorImpl::CreateStringSensor(const std::string& path, const std::string& description)
	{
		return StringSensor(std::make_shared<HSMSensorImpl<std::string>>(collector_.CreateStringSensor(path, DescriptionOptions(description))));
	}

	StringSensor DataCollectorImpl::CreateStringSensor(const std::string& path, const HSMInstantSensorOptions& options)
	{
		auto native = collector_.CreateStringSensor(path, ToNativeInstantOptions(options));
		for (const auto& alert : options.alerts)
			AttachWrapperAlert(collector_, native, alert.Impl()->Data());
		return StringSensor(std::make_shared<HSMSensorImpl<std::string>>(std::move(native)));
	}

	IntBarSensor DataCollectorImpl::CreateIntBarSensor(const std::string& path, int timeout, int small_period, const std::string& description)
	{
		hsm::collector::BarOptions options;
		options.bar_period = std::chrono::milliseconds(timeout);
		options.post_period = std::chrono::milliseconds(small_period);
		options.description = description;
		return IntBarSensor(std::make_shared<HSMBarSensorImpl<int>>(collector_.CreateIntBarSensor(path, options)));
	}

	IntBarSensor DataCollectorImpl::CreateIntBarSensor(const std::string& path, const HSMBarSensorOptions& options)
	{
		auto native = collector_.CreateIntBarSensor(path, ToNativeBarOptions(options));
		for (const auto& alert : options.alerts)
			AttachWrapperAlert(collector_, native, alert.Impl()->Data());
		return IntBarSensor(std::make_shared<HSMBarSensorImpl<int>>(std::move(native)));
	}

	DoubleBarSensor DataCollectorImpl::CreateDoubleBarSensor(const std::string& path, int timeout, int small_period, int precision, const std::string& description)
	{
		hsm::collector::BarOptions options;
		options.bar_period = std::chrono::milliseconds(timeout);
		options.post_period = std::chrono::milliseconds(small_period);
		options.precision = precision;
		options.description = description;
		return DoubleBarSensor(std::make_shared<HSMBarSensorImpl<double>>(collector_.CreateDoubleBarSensor(path, options)));
	}

	DoubleBarSensor DataCollectorImpl::CreateDoubleBarSensor(const std::string& path, const HSMBarSensorOptions& options)
	{
		auto native = collector_.CreateDoubleBarSensor(path, ToNativeBarOptions(options));
		for (const auto& alert : options.alerts)
			AttachWrapperAlert(collector_, native, alert.Impl()->Data());
		return DoubleBarSensor(std::make_shared<HSMBarSensorImpl<double>>(std::move(native)));
	}

	IntRateSensor DataCollectorImpl::CreateIntRateSensor(const std::string& path, int period, const std::string& description)
	{
		hsm::collector::RateOptions options;
		options.post_period = std::chrono::milliseconds(period);
		options.description = description;
		return IntRateSensor(std::make_shared<HSMRateSensorImpl<int>>(collector_.CreateRateSensor(path, options)));
	}

	IntRateSensor DataCollectorImpl::CreateIntRateSensor(const std::string& path, const HSMRateSensorOptions& options)
	{
		auto native = collector_.CreateRateSensor(path, ToNativeRateOptions(options));
		for (const auto& alert : options.alerts)
			AttachWrapperAlert(collector_, native, alert.Impl()->Data());
		return IntRateSensor(std::make_shared<HSMRateSensorImpl<int>>(std::move(native)));
	}

	DoubleRateSensor DataCollectorImpl::CreateDoubleRateSensor(const std::string& path, int period, const std::string& description)
	{
		hsm::collector::RateOptions options;
		options.post_period = std::chrono::milliseconds(period);
		options.description = description;
		return DoubleRateSensor(std::make_shared<HSMRateSensorImpl<double>>(collector_.CreateRateSensor(path, options)));
	}

	DoubleRateSensor DataCollectorImpl::CreateDoubleRateSensor(const std::string& path, const HSMRateSensorOptions& options)
	{
		auto native = collector_.CreateRateSensor(path, ToNativeRateOptions(options));
		for (const auto& alert : options.alerts)
			AttachWrapperAlert(collector_, native, alert.Impl()->Data());
		return DoubleRateSensor(std::make_shared<HSMRateSensorImpl<double>>(std::move(native)));
	}

	BoolLastValueSensor DataCollectorImpl::CreateLastValueBoolSensor(const std::string& path, bool default_value, const std::string& /*description*/)
	{
		return BoolLastValueSensor(std::make_shared<HSMLastValueSensorImpl<bool>>(collector_.CreateLastValueBoolSensor(path, default_value)));
	}

	IntLastValueSensor DataCollectorImpl::CreateLastValueIntSensor(const std::string& path, int default_value, const std::string& /*description*/)
	{
		return IntLastValueSensor(std::make_shared<HSMLastValueSensorImpl<int>>(collector_.CreateLastValueIntSensor(path, default_value)));
	}

	DoubleLastValueSensor DataCollectorImpl::CreateLastValueDoubleSensor(const std::string& path, double default_value, const std::string& /*description*/)
	{
		return DoubleLastValueSensor(std::make_shared<HSMLastValueSensorImpl<double>>(collector_.CreateLastValueDoubleSensor(path, default_value)));
	}

	StringLastValueSensor DataCollectorImpl::CreateLastValueStringSensor(const std::string& path, const std::string& default_value, const std::string& /*description*/)
	{
		return StringLastValueSensor(std::make_shared<HSMLastValueSensorImpl<std::string>>(collector_.CreateLastValueStringSensor(path, default_value)));
	}
}
