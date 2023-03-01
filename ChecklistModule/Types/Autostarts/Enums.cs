﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ChecklistModule.Types.Autostarts
{
  public enum AutostartConditionOperator
  {
    And,
    Or
  }

  public enum AutostartPropertyDirection
  {
    Above,
    Below,
    Passing
  }

  public enum AutostartPropertyName
  {
    Altitude,
    Height,
    GS,
    IAS,
    Bank,
    ParkingBrakeSet,
    PushbackTugConnected,
    EngineStarted,
    VerticalSpeed,
    Acceleration
  }
}
