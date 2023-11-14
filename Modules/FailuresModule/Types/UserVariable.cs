﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FailuresModule.Types
{
  public class UserVariable : Variable
  {
    public double DefaultValue { get; set; }
    public double Value { get; set; }
    public override double GetValue() => this.Value;
  }
}