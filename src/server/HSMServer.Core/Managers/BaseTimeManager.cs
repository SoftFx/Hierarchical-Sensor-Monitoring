﻿using HSMServer.Core.Model.Policies;
using NLog;
using System;
using System.Collections.Generic;

namespace HSMServer.Core.Managers
{
    internal abstract class BaseTimeManager
    {
        protected readonly Logger _logger;


        internal event Action<AlertMessage> NewMessageEvent; 


        internal abstract void FlushMessages();

        protected BaseTimeManager()
        {
            _logger = LogManager.GetLogger(GetType().Name);
        }

        protected void SendAlertMessage(AlertMessage message)
        {
            if (!message.IsEmpty)
                NewMessageEvent?.Invoke(message);
        }

        protected void SendAlertMessage(Guid sensorId, List<AlertResult> alerts) =>
            SendAlertMessage(new AlertMessage(sensorId, alerts));
    }
}