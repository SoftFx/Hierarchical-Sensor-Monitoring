using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace HSMServer
{
    public enum ReturnCodes
    {
        Success,
        IncorrectKey,
        UnknownError,
        FailedToGetData,
        NoSensorForGivenMachine,
        NoDataFound
    }
}
