#pragma once

// Native backend for the alert DSL + sensor-options structs. The managed build accumulated managed
// AlertConditionTemplate/AlertTemplate objects; here we accumulate plain data and convert it to a
// native hsm::collector alert (via Collector::CreateAlert + Sensor::AttachAlert) at sensor creation,
// because the native AlertBuilder is collector-bound while the public AlertsBuilder is static.

#include "HSMSensorOptions.h"

#include "hsm_collector/hsm_collector.hpp"

#include <memory>
#include <optional>
#include <string>
#include <vector>

namespace hsm_wrapper
{
	struct WrapperAlertCondition
	{
		HSMAlertProperty property;
		HSMAlertOperation operation;
		HSMTargetType target;
		std::string value;
	};

	enum class WrapperAlertKind
	{
		Instant,
		Bar,
	};

	// Finalized alert data; converted to a native alert and attached to a sensor at creation time.
	struct WrapperAlertData
	{
		WrapperAlertKind kind = WrapperAlertKind::Instant;
		std::vector<WrapperAlertCondition> conditions;
		std::string notification_template;
		bool has_template = false;
		bool sensor_error = false;
		bool disabled = false;
		std::optional<HSMAlertIcon> builtin_icon;
		std::optional<std::wstring> raw_icon;
	};

	class HSMAlertBaseTemplateImpl
	{
	public:
		explicit HSMAlertBaseTemplateImpl(WrapperAlertKind kind)
		{
			data_.kind = kind;
		}

		WrapperAlertData& Data()
		{
			return data_;
		}
		const WrapperAlertData& Data() const
		{
			return data_;
		}

	private:
		WrapperAlertData data_;
	};

	class HSMAlertConditionBaseImpl
	{
	public:
		void BuildCondition(HSMAlertProperty property, HSMAlertOperation operation, HSMTargetType target, std::string value)
		{
			conditions_.push_back({ property, operation, target, std::move(value) });
		}

		const std::vector<WrapperAlertCondition>& Conditions() const
		{
			return conditions_;
		}

	private:
		std::vector<WrapperAlertCondition> conditions_;
	};

	class HSMAlertActionImpl : public IHSMAlertActionImpl
	{
	public:
		explicit HSMAlertActionImpl(std::shared_ptr<HSMAlertConditionBaseImpl> pimpl) : conditions_(pimpl->Conditions())
		{
		}

		void AndSendNotification(std::string notification_template) override
		{
			template_ = std::move(notification_template);
			has_template_ = true;
		}
		void AndSetIcon(std::wstring icon) override
		{
			raw_icon_ = std::move(icon);
		}
		void AndSetIcon(HSMAlertIcon icon) override
		{
			builtin_icon_ = icon;
		}
		void AndSetSensorError() override
		{
			sensor_error_ = true;
		}
		void BuildAndDisable() override
		{
			disabled_ = true;
		}

		void CopyInto(WrapperAlertData& data) const
		{
			data.conditions = conditions_;
			data.notification_template = template_;
			data.has_template = has_template_;
			data.sensor_error = sensor_error_;
			data.disabled = disabled_;
			data.builtin_icon = builtin_icon_;
			data.raw_icon = raw_icon_;
		}

	private:
		std::vector<WrapperAlertCondition> conditions_;
		std::string template_;
		bool has_template_ = false;
		bool sensor_error_ = false;
		bool disabled_ = false;
		std::optional<HSMAlertIcon> builtin_icon_;
		std::optional<std::wstring> raw_icon_;
	};

	// Build a native alert from WrapperAlertData on `collector` and attach it to `sensor`.
	void AttachWrapperAlert(hsm::collector::Collector& collector, hsm::collector::Sensor& sensor, const WrapperAlertData& data);

	// Map the wrapper option structs onto the native option structs (alerts handled separately).
	hsm::collector::SensorOptions ToNativeInstantOptions(const HSMInstantSensorOptions& options);
	hsm::collector::BarOptions ToNativeBarOptions(const HSMBarSensorOptions& options);
	hsm::collector::RateOptions ToNativeRateOptions(const HSMRateSensorOptions& options);
}
