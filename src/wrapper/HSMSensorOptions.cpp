#include "pch.h"

#include "HSMSensorOptions.h"

#include "HSMSensorOptionsImpl.h"

hsm_wrapper::HSMAlertBaseTemplateImpl::HSMAlertBaseTemplateImpl(AlertBaseTemplate^ alert) : alert_{alert}
{
}

hsm_wrapper::HSMAlertBaseTemplateImpl::~HSMAlertBaseTemplateImpl()
{

}


std::shared_ptr<hsm_wrapper::HSMAlertConditionBaseImpl> hsm_wrapper::CreateHSMAlertConditionBaseImpl()
{
	return std::make_shared<hsm_wrapper::HSMAlertConditionBaseImpl>();
}

void hsm_wrapper::BuildHSMCondition(std::shared_ptr<HSMAlertConditionBaseImpl> impl, HSMAlertProperty property, HSMAlertOperation operation, HSMTargetType target, std::string value /*= {}*/)
{
	impl->BuildCondition(property, operation, target, value);
}

std::shared_ptr<hsm_wrapper::IHSMAlertActionImpl> hsm_wrapper::CreateHSMAlertActionImpl(std::shared_ptr<HSMAlertConditionBaseImpl> pimpl)
{
	return std::make_shared<hsm_wrapper::HSMAlertActionImpl>(pimpl);
}

template <class T, class A>
void hsm_wrapper::CopyActionToAlert(T ptemplate, A pimpl)
{
	ptemplate->Conditions = pimpl->conditions_.get();
	ptemplate->Template = pimpl->template_.get();
	ptemplate->Status = pimpl->status_;
	ptemplate->Icon = pimpl->icon_.get();
	ptemplate->IsDisabled = pimpl->is_disabled_;
}

void hsm_wrapper::BuildAlertTemplate(std::shared_ptr<IHSMAlertActionImpl> piimpl, HSMInstantAlertTemplate& alert)
{
	auto pimpl = std::dynamic_pointer_cast<HSMAlertActionImpl>(piimpl);
	auto ptemplate = alert.Impl()->GetAlert<InstantAlertTemplate>();
	CopyActionToAlert(ptemplate, pimpl);
}

HSMWRAPPER_API void hsm_wrapper::BuildAlertTemplate(std::shared_ptr<IHSMAlertActionImpl> piimpl, HSMBarAlertTemplate& alert)
{
	auto pimpl = std::dynamic_pointer_cast<HSMAlertActionImpl>(piimpl);
	auto ptemplate = alert.Impl()->GetAlert<BarAlertTemplate>();
	CopyActionToAlert(ptemplate, pimpl);
}

HSMWRAPPER_API std::shared_ptr<hsm_wrapper::HSMAlertBaseTemplateImpl> hsm_wrapper::CreateHSMInstantAlertBaseTemplateImpl()
{
	auto ptr = new hsm_wrapper::HSMAlertBaseTemplateImpl(gcnew InstantAlertTemplate());
	return std::shared_ptr<hsm_wrapper::HSMAlertBaseTemplateImpl>(ptr);
}

HSMWRAPPER_API std::shared_ptr<hsm_wrapper::HSMAlertBaseTemplateImpl> hsm_wrapper::CreateHSMBarAlertBaseTemplateImpl()
{
	auto ptr = new hsm_wrapper::HSMAlertBaseTemplateImpl(gcnew BarAlertTemplate());
	return std::shared_ptr<hsm_wrapper::HSMAlertBaseTemplateImpl>(ptr);
}
