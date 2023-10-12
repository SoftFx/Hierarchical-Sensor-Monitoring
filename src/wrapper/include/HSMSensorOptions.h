#pragma once

#include "HSMEnums.h"

namespace hsm_wrapper
{
	class HSMInstantAlertTemplate;
	class HSMBarAlertTemplate;

	class HSMAlertConditionBaseImpl;
	HSMWRAPPER_API std::shared_ptr<HSMAlertConditionBaseImpl> CreateHSMAlertConditionBaseImpl();
	HSMWRAPPER_API void BuildHSMCondition(std::shared_ptr<HSMAlertConditionBaseImpl> impl,
		HSMAlertProperty property, HSMAlertOperation operation, HSMTargetType target, std::string value = {});

	class IHSMAlertActionImpl
	{
	public:
		virtual void AndSendNotification(std::string notification_template) = 0;
		virtual void AndSetIcon(std::wstring icon) = 0; // for unicode support
		virtual void AndSetIcon(HSMAlertIcon icon) = 0;
		virtual void AndSetSensorError() = 0;
		virtual void BuildAndDisable() = 0;
		inline virtual ~IHSMAlertActionImpl() {};
	};

	HSMWRAPPER_API std::shared_ptr<IHSMAlertActionImpl> CreateHSMAlertActionImpl(std::shared_ptr<HSMAlertConditionBaseImpl> pimpl);

	HSMWRAPPER_API void BuildAlertTemplate(std::shared_ptr<IHSMAlertActionImpl> pimpl, HSMInstantAlertTemplate& alert);
	HSMWRAPPER_API void BuildAlertTemplate(std::shared_ptr<IHSMAlertActionImpl> pimpl, HSMBarAlertTemplate& alert);

	template <class T>
	class HSMAlertAction 
	{
	protected:
		std::shared_ptr<IHSMAlertActionImpl> pimpl_;
	public:
		HSMAlertAction(std::shared_ptr<HSMAlertConditionBaseImpl> pimpl) : pimpl_{ CreateHSMAlertActionImpl(pimpl)}
		{
		};
		
		HSMAlertAction<T>& AndSendNotification(std::string notification_template)
		{
			pimpl_->AndSendNotification(move(notification_template));
			return *this;
		}

		HSMAlertAction<T>& AndSetIcon(std::wstring icon)
		{
			pimpl_->AndSetIcon(move(icon));
			return *this;
		}

		HSMAlertAction<T>& AndSetIcon(HSMAlertIcon icon)
		{
			pimpl_->AndSetIcon(icon);
			return *this;
		}		

		HSMAlertAction<T>& AndSetSensorError()
		{
			pimpl_->AndSetSensorError();
			return *this;
		}

		T BuildAndDisable()
		{
			pimpl_->BuildAndDisable();
			return Build();
		}

		T Build() 
		{
			T alert{};
			BuildAlertTemplate(pimpl_, alert);
			return alert;
		}
	};

	template <class T>
	class HSMAlertConditionBase
	{
		std::shared_ptr<HSMAlertConditionBaseImpl> pimpl_;
	protected:
		HSMAlertConditionBase()
		{
			pimpl_ = CreateHSMAlertConditionBaseImpl();
		}

	public:

		HSMAlertAction<T> ThenSendNotification(std::string template_name)
		{
			return BuildAlertAction().AndSendNotification(move(template_name));
		}

		HSMAlertAction<T> ThenSetIcon(std::wstring icon)
		{
			return BuildAlertAction().AndSetIcon(move(icon));
		}

		HSMAlertAction<T> ThenSetIcon(HSMAlertIcon icon)
		{
			return BuildAlertAction().AndSetIcon(icon);
		}

		HSMAlertAction<T> ThenSetSensorError()
		{
			return  BuildAlertAction().AndSetSensorError();
		}

	protected:

		virtual HSMAlertAction<T> BuildAlertAction()
		{
			return HSMAlertAction<T>(pimpl_);
		};

		void BuildConstCondition(HSMAlertProperty property, HSMAlertOperation operation, std::string value = {})
		{
			BuildCondition(property, operation, HSMTargetType::Const, move(value));
		}

		void BuildLastValueCondition(HSMAlertProperty property, HSMAlertOperation operation)
		{
			BuildCondition(property, operation, HSMTargetType::LastValue);
		}

		void BuildCondition(HSMAlertProperty property, HSMAlertOperation operation, HSMTargetType target, std::string value = {})
		{
			BuildHSMCondition(pimpl_, property, operation, target, move(value));
		}
	};

	template <class D, class T>
	class HSMDataAlertCondition : public HSMAlertConditionBase<T>
	{
	private:
		D* Derived()
		{
			return static_cast<D*>(this);
		}
	public:
		D& AndReceivedNewValue()
		{
			this->BuildLastValueCondition(HSMAlertProperty::NewSensorData, HSMAlertOperation::ReceivedNewValue);
			return *Derived();
		}

		D& AndComment(HSMAlertOperation operation, std::string target = {})
		{
			if (operation == HSMAlertOperation::IsChanged)
				this->BuildLastValueCondition(HSMAlertProperty::Comment, operation);
			else
				this->BuildConstCondition(HSMAlertProperty::Comment, operation, target);

			return *Derived();
		}

		D& AndStatus(HSMAlertOperation operation)
		{
			this->BuildLastValueCondition(HSMAlertProperty::Status, operation);
			return *Derived();
		}
	};

	class HSMWRAPPER_API HSMInstantAlertCondition : public HSMDataAlertCondition<HSMInstantAlertCondition, HSMInstantAlertTemplate>
	{
	private:
		inline HSMInstantAlertCondition() {};
		friend class AlertsBuilder;
	public:
		template <class T>
		HSMInstantAlertCondition& AndValue(HSMAlertOperation operation, T target)
		{
			this->BuildConstCondition(HSMAlertProperty::Value, operation, std::to_string(target));
			return *this;
		}
		template <class T>
		HSMInstantAlertCondition& AndLength(HSMAlertOperation operation, T target)
		{
			this->BuildConstCondition(HSMAlertProperty::Length, operation, std::to_string(target));
			return *this;
		}

		template <class T>
		HSMInstantAlertCondition& AndFileSize(HSMAlertOperation operation, T target)
		{
			this->BuildConstCondition(HSMAlertProperty::OriginalSize, operation, std::to_string(target));
			return *this;
		}
	};

	class HSMWRAPPER_API HSMBarAlertCondition : public HSMDataAlertCondition<HSMBarAlertCondition, HSMBarAlertTemplate>
	{
	private:
		inline HSMBarAlertCondition() {};
		friend class AlertsBuilder;
	public:
		template<class T>
		HSMBarAlertCondition AndMax(HSMAlertOperation operation, T target)
		{
			this->BuildConstCondition(HSMAlertProperty::Max, operation, std::to_string(target));
			return *this;
		}

		template<class T>
		HSMBarAlertCondition AndMean(HSMAlertOperation operation, T target)
		{
			this->BuildConstCondition(HSMAlertProperty::Mean, operation, std::to_string(target));
			return *this;
		}

		template<class T>
		HSMBarAlertCondition AndMin(HSMAlertOperation operation, T target) 
		{
			this->BuildConstCondition(HSMAlertProperty::Min, operation, std::to_string(target));
			return *this;
		}

		template<class T>
		HSMBarAlertCondition AndLastValue(HSMAlertOperation operation, T target)
		{
			this->BuildConstCondition(HSMAlertProperty::LastValue, operation, std::to_string(target));
			return *this;
		}

		HSMBarAlertCondition AndCount(HSMAlertOperation operation, int target)
		{
			this->BuildConstCondition(HSMAlertProperty::Count, operation, std::to_string(target));
			return *this;
		}
	};

	class HSMWRAPPER_API AlertsBuilder
	{
	public:
		template <class T>
		static HSMInstantAlertCondition IfValue(HSMAlertOperation operation, T target)
		{
			return HSMInstantAlertCondition().AndValue(operation, std::move(target));
		};

		template <class T>
		static HSMInstantAlertCondition IfLenght(HSMAlertOperation operation, T target)
 		{
			return HSMInstantAlertCondition().AndLength(operation, std::move(target));
 		}

		template <class T>
		static HSMInstantAlertCondition IfFileSize(HSMAlertOperation operation, T target)
 		{
			return HSMInstantAlertCondition().AndFileSize(operation, std::move(target));
 		}

		static HSMInstantAlertCondition IfReceivedNewValue()
		{
			return HSMInstantAlertCondition().AndReceivedNewValue();
		}

		static HSMInstantAlertCondition IfComment(HSMAlertOperation operation, std::string target = {})
		{
			return HSMInstantAlertCondition().AndComment(operation, target);
		}

		static HSMInstantAlertCondition IfStatus(HSMAlertOperation operation)
		{
			return HSMInstantAlertCondition().AndStatus(operation);
		}

		template<class T>
		static HSMBarAlertCondition IfMax(HSMAlertOperation operation, T value) 
 		{
 			return HSMBarAlertCondition().AndMax(operation, std::move(value));
 		}

		template<class T>
		static HSMBarAlertCondition IfMean(HSMAlertOperation operation, T value) 
 		{
 			return HSMBarAlertCondition().AndMean(operation, std::move(value));
 		}
		
		template<class T>
		static HSMBarAlertCondition IfMin(HSMAlertOperation operation, T value) 
 		{
 			return HSMBarAlertCondition().AndMin(operation, std::move(value));
 		}

		template<class T>
		static HSMBarAlertCondition IfLastValue(HSMAlertOperation operation, T value) 
 		{
 			return HSMBarAlertCondition().AndLastValue(operation, std::move(value));
 		}

		static HSMBarAlertCondition IfCount(HSMAlertOperation operation, int value)
 		{
 			return HSMBarAlertCondition().AndCount(operation, value);
 		}

		static HSMBarAlertCondition IfBarComment(HSMAlertOperation operation, std::string target = {})
 		{
 			return HSMBarAlertCondition().AndComment(operation, std::move(target));
 		}

		static HSMBarAlertCondition IfBarStatus(HSMAlertOperation operation)
 		{
 			return HSMBarAlertCondition().AndStatus(operation);
 		}

		static HSMBarAlertCondition IfReceivedNewBarValue()
 		{
 			return HSMBarAlertCondition().AndReceivedNewValue();
 		}
	};

	class HSMAlertBaseTemplateImpl;
	HSMWRAPPER_API std::shared_ptr<HSMAlertBaseTemplateImpl> CreateHSMInstantAlertBaseTemplateImpl();
	HSMWRAPPER_API std::shared_ptr<HSMAlertBaseTemplateImpl> CreateHSMBarAlertBaseTemplateImpl();

	class HSMWRAPPER_API HSMAlertBaseTemplate
	{
	protected:
		HSMAlertBaseTemplate(std::shared_ptr<HSMAlertBaseTemplateImpl> pimpl) : pimpl_{ pimpl }
		{};
		std::shared_ptr<HSMAlertBaseTemplateImpl> pimpl_;
	public:
		std::shared_ptr<HSMAlertBaseTemplateImpl> Impl() const
		{
			return pimpl_;
		};
	};

	class HSMWRAPPER_API HSMInstantAlertTemplate final : public HSMAlertBaseTemplate
	{ 
	public:
		HSMInstantAlertTemplate() : HSMAlertBaseTemplate(CreateHSMInstantAlertBaseTemplateImpl()) {};
	};


	class HSMWRAPPER_API HSMBarAlertTemplate final : public HSMAlertBaseTemplate
	{ 
	public:
		HSMBarAlertTemplate() : HSMAlertBaseTemplate(CreateHSMBarAlertBaseTemplateImpl()) {};
	};

	class HSMWRAPPER_API HSMInstantSensorOptions
	{
	public:
		std::string description;
		std::vector<HSMInstantAlertTemplate> alerts;
	};

	class HSMWRAPPER_API HSMBarSensorOptions
	{
	public:
		int bar_period = 30000;
		int post_data_period = 15000;
		int precision = 2;
		std::string description;
		std::vector<HSMBarAlertTemplate> alerts;
	};

}