﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FailuresModule.Types.RunVM
{
  internal class RunIncidentDefinition : RunIncident
  {
    internal static RunIncident Create(IncidentDefinition id)
    {
      RunIncidentDefinition ret = new RunIncidentDefinition(id);
      return ret;
    }

    private RunIncidentDefinition(IncidentDefinition incidentDefinition)
    {
      IncidentDefinition = incidentDefinition;
    }

    public IncidentDefinition IncidentDefinition { get; }
    public List<RunTriggerEvaluation> TriggerEvaluations { get; } = new List<RunTriggerEvaluation>();
  }
}