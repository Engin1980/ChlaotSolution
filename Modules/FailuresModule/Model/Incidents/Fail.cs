﻿using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Eng.Chlaot.Modules.FailuresModule.Model.Incidents
{
    public abstract class Fail
    {
        public double Weight { get; set; } = 1;
    }
}
