#pragma once

namespace hsm_wrapper
{
	enum class HSMTargetType : unsigned char
	{
		Const,
		LastValue,
	};

	enum class HSMAlertOperation : unsigned char
	{
		LessThanOrEqual = 0,
		LessThan = 1,
		GreaterThan = 2,
		GreaterThanOrEqual = 3,
		Equal = 4,
		NotEqual = 5,

		IsChanged = 20,
		IsError = 21,
		IsOk = 22,
		IsChangedToError = 23,
		IsChangedToOk = 24,

		Contains = 30,
		StartsWith = 31,
		EndsWith = 32,

		ReceivedNewValue = 50,
	};


	enum class HSMAlertProperty : unsigned char
	{
		Status = 0,
		Comment = 1,

		Value = 20,

		Min = 101,
		Max = 102,
		Mean = 103,
		Count = 104,
		LastValue = 105,

		Length = 120,

		OriginalSize = 151,

		NewSensorData = 200,
	};

	enum class HSMAlertCombination : unsigned char
	{
		And,
		Or,
	};
	
	enum class HSMSensorStatus
	{
		OffTime = 0,
		Ok = 1,
		Warning = 2,
		Error = 3
	};

	enum class HSMAlertIcon
	{
		Ok = 0,
		Warning = 1,
		Error = 2,
		Pause = 3,

		ArrowUp = 10,
		ArrowDown = 11,

		Clock = 100,
		Hourglass = 101,
	};

	enum class HSMUnit
	{
		bits = 0,
		bytes = 1,
		KB = 2,
		MB = 3,
		GB = 4,
		Percents = 100,
		Ticks = 1000,
		Milliseconds = 1010,
		Seconds = 1011,
		Minutes = 1012
	};

// 	class HSMAlertIcon
// 	{
// 	public:
// 		static const std::wstring Ok = "✅";
// 		static const std::wstring Warning = "⚠";
// 		static const std::wstring Error = "❌";
// 		static const std::wstring Pause = "⏸";
// 		static const std::wstring ArrowUp = "⬆";
// 		static const std::wstring ArrowDown = "⬇";
// 		static const std::wstring Clock = "🕐";
// 		static const std::wstring Hourglass = "⌛";
// 	};
}
