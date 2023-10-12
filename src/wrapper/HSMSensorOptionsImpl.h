#pragma once

#include <msclr/auto_gcroot.h>

using namespace HSMDataCollector::Alerts;
using namespace HSMDataCollector;
using namespace HSMSensorDataObjects::SensorRequests;
using namespace System::Collections::Generic;

namespace hsm_wrapper
{
	class HSMAlertBaseTemplateImpl
	{
	private:
		msclr::auto_gcroot<AlertBaseTemplate^> alert_;
	public:
		HSMAlertBaseTemplateImpl(AlertBaseTemplate^ alert);
		~HSMAlertBaseTemplateImpl();

		template <class T>
		T^ GetAlert() { return static_cast<T^>(alert_.get()); };
	};

	class HSMAlertActionImpl;
	
	class HSMAlertConditionBaseImpl
	{
	private:
		msclr::auto_gcroot<List<AlertConditionTemplate^>^> conditions_;
	public:
		HSMAlertConditionBaseImpl() 

		{
			conditions_ = gcnew List<AlertConditionTemplate^>();
		};

		inline void BuildCondition(HSMAlertProperty property, HSMAlertOperation operation, HSMTargetType target, std::string value) const
		{
			AlertTargetTemplate^ gc_target = AlertTargetTemplate::Build(static_cast<TargetType>(static_cast<int>(target)), gcnew System::String(value.c_str()));
			AlertConditionTemplate^ condition = AlertConditionTemplate::Build(gc_target, AlertCombination::And, static_cast<AlertOperation>(static_cast<int>(operation)), static_cast<AlertProperty>(static_cast<int>(property)));
			conditions_->Add(condition);
		}

		friend HSMAlertActionImpl;
	};


	class HSMAlertActionImpl : public IHSMAlertActionImpl
	{
	private:
		msclr::auto_gcroot<List<AlertConditionTemplate^>^> conditions_;
		msclr::auto_gcroot<System::String^> template_;
		HSMSensorDataObjects::SensorStatus status_ = HSMSensorDataObjects::SensorStatus::Ok;
		msclr::auto_gcroot<System::String^> icon_;
		bool is_disabled_ = false;

	public:
		HSMAlertActionImpl(std::shared_ptr<HSMAlertConditionBaseImpl> pimpl)
		{
			conditions_ = pimpl->conditions_;
		}

		template <class T, class A>
		friend void CopyActionToAlert(T ptemplate, A pimpl);

		//friend void hsm_wrapper::BuildAlertTemplate(std::shared_ptr<IHSMAlertActionImpl> pimpl, HSMInstantAlertTemplate& alert);
		//friend void hsm_wrapper::BuildAlertTemplate(std::shared_ptr<IHSMAlertActionImpl> pimpl, HSMBarAlertTemplate& alert);
		//friend void hsm_wrapper::BuildAlertTemplate(std::shared_ptr<HSMAlertActionImpl> pimpl, HSMSpecialAlertTemplate& alert);

		void AndSendNotification(std::string notification_template) override
		{
			template_ = gcnew System::String(notification_template.c_str());
		}


		void AndSetIcon(std::wstring icon) override
		{
			icon_ = gcnew System::String(icon.c_str()); 
		}


		void AndSetIcon(HSMAlertIcon icon) override
		{
			//icon_ = HSMDataCollector::Extensions::IconExtensions::ToUtf8(static_cast<AlertIcon>(static_cast<int>(icon)));
		}


		void AndSetSensorError() override
		{
			status_ = HSMSensorDataObjects::SensorStatus::Error;
		}


		void BuildAndDisable() override
		{
			is_disabled_ = true;
		}

	};

}

