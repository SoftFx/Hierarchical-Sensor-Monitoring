#include "pch.h"

#include "HSMSensorOptions.h"
#include "HSMSensorOptionsImpl.h"

#ifdef _WIN32
#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <windows.h>
#endif

namespace hsm_wrapper
{
	// The wrapper enums are cast straight to the native hsm::collector enums across the boundary
	// (ToNativeStatus, AttachWrapperAlert, the sensor AddValue paths). That is only correct while the
	// numeric values match exactly — pin the contract so a renumber on either side fails to compile
	// here instead of silently producing wrong statuses/alerts.
#define HSM_ASSERT_ENUM_MATCH(wrapper_value, native_value) \
	static_assert(static_cast<int>(wrapper_value) == static_cast<int>(native_value), "enum value drift: " #wrapper_value " != " #native_value)

	HSM_ASSERT_ENUM_MATCH(HSMSensorStatus::OffTime, hsm::collector::SensorStatus::OffTime);
	HSM_ASSERT_ENUM_MATCH(HSMSensorStatus::Ok, hsm::collector::SensorStatus::Ok);
	HSM_ASSERT_ENUM_MATCH(HSMSensorStatus::Warning, hsm::collector::SensorStatus::Warning);
	HSM_ASSERT_ENUM_MATCH(HSMSensorStatus::Error, hsm::collector::SensorStatus::Error);

	// Every HSMAlertOperation enumerator (condition.operation is cast straight through).
	HSM_ASSERT_ENUM_MATCH(HSMAlertOperation::LessThanOrEqual, hsm::collector::AlertOperation::LessThanOrEqual);
	HSM_ASSERT_ENUM_MATCH(HSMAlertOperation::LessThan, hsm::collector::AlertOperation::LessThan);
	HSM_ASSERT_ENUM_MATCH(HSMAlertOperation::GreaterThan, hsm::collector::AlertOperation::GreaterThan);
	HSM_ASSERT_ENUM_MATCH(HSMAlertOperation::GreaterThanOrEqual, hsm::collector::AlertOperation::GreaterThanOrEqual);
	HSM_ASSERT_ENUM_MATCH(HSMAlertOperation::Equal, hsm::collector::AlertOperation::Equal);
	HSM_ASSERT_ENUM_MATCH(HSMAlertOperation::NotEqual, hsm::collector::AlertOperation::NotEqual);
	HSM_ASSERT_ENUM_MATCH(HSMAlertOperation::IsChanged, hsm::collector::AlertOperation::IsChanged);
	HSM_ASSERT_ENUM_MATCH(HSMAlertOperation::IsError, hsm::collector::AlertOperation::IsError);
	HSM_ASSERT_ENUM_MATCH(HSMAlertOperation::IsOk, hsm::collector::AlertOperation::IsOk);
	HSM_ASSERT_ENUM_MATCH(HSMAlertOperation::IsChangedToError, hsm::collector::AlertOperation::IsChangedToError);
	HSM_ASSERT_ENUM_MATCH(HSMAlertOperation::IsChangedToOk, hsm::collector::AlertOperation::IsChangedToOk);
	HSM_ASSERT_ENUM_MATCH(HSMAlertOperation::Contains, hsm::collector::AlertOperation::Contains);
	HSM_ASSERT_ENUM_MATCH(HSMAlertOperation::StartsWith, hsm::collector::AlertOperation::StartsWith);
	HSM_ASSERT_ENUM_MATCH(HSMAlertOperation::EndsWith, hsm::collector::AlertOperation::EndsWith);
	HSM_ASSERT_ENUM_MATCH(HSMAlertOperation::ReceivedNewValue, hsm::collector::AlertOperation::ReceivedNewValue);

	// Every HSMAlertProperty enumerator (condition.property is cast straight through).
	HSM_ASSERT_ENUM_MATCH(HSMAlertProperty::Status, hsm::collector::AlertProperty::Status);
	HSM_ASSERT_ENUM_MATCH(HSMAlertProperty::Comment, hsm::collector::AlertProperty::Comment);
	HSM_ASSERT_ENUM_MATCH(HSMAlertProperty::Value, hsm::collector::AlertProperty::Value);
	HSM_ASSERT_ENUM_MATCH(HSMAlertProperty::Min, hsm::collector::AlertProperty::Min);
	HSM_ASSERT_ENUM_MATCH(HSMAlertProperty::Max, hsm::collector::AlertProperty::Max);
	HSM_ASSERT_ENUM_MATCH(HSMAlertProperty::Mean, hsm::collector::AlertProperty::Mean);
	HSM_ASSERT_ENUM_MATCH(HSMAlertProperty::Count, hsm::collector::AlertProperty::Count);
	HSM_ASSERT_ENUM_MATCH(HSMAlertProperty::LastValue, hsm::collector::AlertProperty::LastValue);
	HSM_ASSERT_ENUM_MATCH(HSMAlertProperty::Length, hsm::collector::AlertProperty::Length);
	HSM_ASSERT_ENUM_MATCH(HSMAlertProperty::OriginalSize, hsm::collector::AlertProperty::OriginalSize);
	HSM_ASSERT_ENUM_MATCH(HSMAlertProperty::NewSensorData, hsm::collector::AlertProperty::NewSensorData);

	// Every HSMAlertIcon enumerator (builtin_icon is cast straight through).
	HSM_ASSERT_ENUM_MATCH(HSMAlertIcon::Ok, hsm::collector::AlertIcon::Ok);
	HSM_ASSERT_ENUM_MATCH(HSMAlertIcon::Warning, hsm::collector::AlertIcon::Warning);
	HSM_ASSERT_ENUM_MATCH(HSMAlertIcon::Error, hsm::collector::AlertIcon::Error);
	HSM_ASSERT_ENUM_MATCH(HSMAlertIcon::Pause, hsm::collector::AlertIcon::Pause);
	HSM_ASSERT_ENUM_MATCH(HSMAlertIcon::ArrowUp, hsm::collector::AlertIcon::ArrowUp);
	HSM_ASSERT_ENUM_MATCH(HSMAlertIcon::ArrowDown, hsm::collector::AlertIcon::ArrowDown);
	HSM_ASSERT_ENUM_MATCH(HSMAlertIcon::Clock, hsm::collector::AlertIcon::Clock);
	HSM_ASSERT_ENUM_MATCH(HSMAlertIcon::Hourglass, hsm::collector::AlertIcon::Hourglass);

	HSM_ASSERT_ENUM_MATCH(HSMTargetType::Const, hsm::collector::AlertTargetType::Const);
	HSM_ASSERT_ENUM_MATCH(HSMTargetType::LastValue, hsm::collector::AlertTargetType::LastValue);
#undef HSM_ASSERT_ENUM_MATCH

	namespace
	{
		std::string WideToUtf8(const std::wstring& text)
		{
#ifdef _WIN32
			if (text.empty())
				return {};
			const int needed = WideCharToMultiByte(CP_UTF8, 0, text.c_str(), static_cast<int>(text.size()), nullptr, 0, nullptr, nullptr);
			if (needed <= 0)
				return {};
			std::string out(static_cast<std::size_t>(needed), '\0');
			WideCharToMultiByte(CP_UTF8, 0, text.c_str(), static_cast<int>(text.size()), out.data(), needed, nullptr, nullptr);
			return out;
#else
			(void)text;
			return {};
#endif
		}

		void BuildAlertTemplateData(std::shared_ptr<IHSMAlertActionImpl> piimpl, HSMAlertBaseTemplate& alert)
		{
			auto pimpl = std::dynamic_pointer_cast<HSMAlertActionImpl>(piimpl);
			if (pimpl)
				pimpl->CopyInto(alert.Impl()->Data());
		}
	}

	std::shared_ptr<HSMAlertConditionBaseImpl> CreateHSMAlertConditionBaseImpl()
	{
		return std::make_shared<HSMAlertConditionBaseImpl>();
	}

	void BuildHSMCondition(std::shared_ptr<HSMAlertConditionBaseImpl> impl, HSMAlertProperty property, HSMAlertOperation operation, HSMTargetType target, std::string value)
	{
		impl->BuildCondition(property, operation, target, std::move(value));
	}

	std::shared_ptr<IHSMAlertActionImpl> CreateHSMAlertActionImpl(std::shared_ptr<HSMAlertConditionBaseImpl> pimpl)
	{
		return std::make_shared<HSMAlertActionImpl>(pimpl);
	}

	void BuildAlertTemplate(std::shared_ptr<IHSMAlertActionImpl> piimpl, HSMInstantAlertTemplate& alert)
	{
		BuildAlertTemplateData(piimpl, alert);
	}

	void BuildAlertTemplate(std::shared_ptr<IHSMAlertActionImpl> piimpl, HSMBarAlertTemplate& alert)
	{
		BuildAlertTemplateData(piimpl, alert);
	}

	std::shared_ptr<HSMAlertBaseTemplateImpl> CreateHSMInstantAlertBaseTemplateImpl()
	{
		return std::make_shared<HSMAlertBaseTemplateImpl>(WrapperAlertKind::Instant);
	}

	std::shared_ptr<HSMAlertBaseTemplateImpl> CreateHSMBarAlertBaseTemplateImpl()
	{
		return std::make_shared<HSMAlertBaseTemplateImpl>(WrapperAlertKind::Bar);
	}

	void AttachWrapperAlert(hsm::collector::Collector& collector, hsm::collector::Sensor& sensor, const WrapperAlertData& data)
	{
		const auto kind = data.kind == WrapperAlertKind::Bar ? hsm::collector::AlertKind::Bar : hsm::collector::AlertKind::Instant;
		auto builder = collector.CreateAlert(kind);
		for (const auto& condition : data.conditions)
		{
			const auto property = static_cast<hsm::collector::AlertProperty>(condition.property);
			const auto operation = static_cast<hsm::collector::AlertOperation>(condition.operation);
			if (condition.target == HSMTargetType::LastValue)
				builder.IfLastValue(property, operation);
			else
				builder.If(property, operation, condition.value);
		}
		if (data.has_template)
			builder.ThenNotify(data.notification_template);
		if (data.sensor_error)
			builder.AsSensorError();
		if (data.builtin_icon.has_value())
			builder.WithIcon(static_cast<hsm::collector::AlertIcon>(*data.builtin_icon));
		else if (data.raw_icon.has_value())
			builder.WithIconRaw(WideToUtf8(*data.raw_icon));
		if (data.disabled)
			builder.Disabled(true);
		sensor.AttachAlert(builder.Build());
	}

	namespace
	{
		// Copy the HSMBaseSensorOptions registration fields shared by every sensor kind.
		template<class NativeOptions>
		void ApplyBaseOptions(NativeOptions& native, const HSMBaseSensorOptions& options)
		{
			native.description = options.description;
			if (options.ttl.has_value())
				native.ttl = *options.ttl;
			if (options.keep_history.has_value())
				native.keep_history = *options.keep_history;
			if (options.self_destroy.has_value())
				native.self_destroy = *options.self_destroy;
			if (options.enable_for_grafana.has_value())
				native.enable_grafana = *options.enable_for_grafana;
			if (options.is_singleton_sensor.has_value())
				native.is_singleton = *options.is_singleton_sensor;
			if (options.aggregate_data.has_value())
				native.aggregate_data = *options.aggregate_data;
			native.default_alert_options = static_cast<hsm::collector::DefaultAlertsOptions>(options.default_alert_options);
		}
	}

	hsm::collector::SensorOptions ToNativeInstantOptions(const HSMInstantSensorOptions& options)
	{
		hsm::collector::SensorOptions native;
		ApplyBaseOptions(native, options);
		return native;
	}

	hsm::collector::BarOptions ToNativeBarOptions(const HSMBarSensorOptions& options)
	{
		hsm::collector::BarOptions native;
		native.bar_period = options.bar_period;
		native.post_period = options.post_data_period;
		native.precision = options.precision;
		ApplyBaseOptions(native, options);
		return native;
	}

	hsm::collector::RateOptions ToNativeRateOptions(const HSMRateSensorOptions& options)
	{
		hsm::collector::RateOptions native;
		native.post_period = options.post_data_period;
		ApplyBaseOptions(native, options);
		return native;
	}
}
