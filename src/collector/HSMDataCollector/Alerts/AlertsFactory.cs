﻿using HSMSensorDataObjects.SensorRequests;
using System;

namespace HSMDataCollector.Alerts
{
    public static class AlertsFactory
    {
        public static SpecialAlertCondition IfInactivityPeriodIs(TimeSpan? time = null)
        {
            return new SpecialAlertCondition().AddTtlValue(time);
        }


        public static InstantAlertCondition IfValue<T>(AlertOperation operation, T target)
        {
            return new InstantAlertCondition().AndValue(operation, target);
        }

        public static InstantAlertCondition IfEmaValue(AlertOperation operation, double target)
        {
            return new InstantAlertCondition().AndEmaValue(operation, target);
        }

        public static InstantAlertCondition IfLenght<T>(AlertOperation operation, T target)
        {
            return new InstantAlertCondition().AndLength(operation, target);
        }

        public static InstantAlertCondition IfFileSize<T>(AlertOperation operation, T target)
        {
            return new InstantAlertCondition().AndFileSize(operation, target);
        }


        public static InstantAlertCondition IfReceivedNewValue()
        {
            return (InstantAlertCondition)new InstantAlertCondition().AndReceivedNewValue();
        }

        public static InstantAlertCondition IfComment(AlertOperation operation, string target = null)
        {
            return (InstantAlertCondition)new InstantAlertCondition().AndComment(operation, target);
        }

        public static InstantAlertCondition IfStatus(AlertOperation operation)
        {
            return (InstantAlertCondition)new InstantAlertCondition().AndStatus(operation);
        }


        public static BarAlertCondition IfMax<T>(AlertOperation operation, T value) where T : struct
        {
            return new BarAlertCondition().AndMax(operation, value);
        }

        public static BarAlertCondition IfMean<T>(AlertOperation operation, T value) where T : struct
        {
            return new BarAlertCondition().AndMean(operation, value);
        }

        public static BarAlertCondition IfMin<T>(AlertOperation operation, T value) where T : struct
        {
            return new BarAlertCondition().AndMin(operation, value);
        }

        public static BarAlertCondition IfFirstValue<T>(AlertOperation operation, T value) where T : struct
        {
            return new BarAlertCondition().AndFirstValue(operation, value);
        }

        public static BarAlertCondition IfLastValue<T>(AlertOperation operation, T value) where T : struct
        {
            return new BarAlertCondition().AndLastValue(operation, value);
        }

        public static BarAlertCondition IfCount(AlertOperation operation, int value)
        {
            return new BarAlertCondition().AndCount(operation, value);
        }

        public static BarAlertCondition IfEmaMin(AlertOperation operation, double value)
        {
            return new BarAlertCondition().AndEmaMin(operation, value);
        }

        public static BarAlertCondition IfEmaMax(AlertOperation operation, double value)
        {
            return new BarAlertCondition().AndEmaMax(operation, value);
        }

        public static BarAlertCondition IfEmaMean(AlertOperation operation, double value)
        {
            return new BarAlertCondition().AndEmaMean(operation, value);
        }

        public static BarAlertCondition IfEmaCount(AlertOperation operation, double value)
        {
            return new BarAlertCondition().AndEmaCount(operation, value);
        }

        public static BarAlertCondition IfBarComment(AlertOperation operation, string target = null)
        {
            return (BarAlertCondition)new BarAlertCondition().AndComment(operation, target);
        }

        public static BarAlertCondition IfBarStatus(AlertOperation operation)
        {
            return (BarAlertCondition)new BarAlertCondition().AndStatus(operation);
        }

        public static BarAlertCondition IfReceivedNewBarValue()
        {
            return (BarAlertCondition)new BarAlertCondition().AndReceivedNewValue();
        }
    }
}