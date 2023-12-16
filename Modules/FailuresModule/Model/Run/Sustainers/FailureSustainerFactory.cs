﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FailuresModule.Model.Sim;

namespace FailuresModule.Types.Run.Sustainers
{
    internal class FailureSustainerFactory
  {
    internal static FailureSustainer Create(FailureDefinition failItem)
    {
      FailureSustainer ret;
      if (failItem is EventFailureDefinition efd)
        ret = CreateEvent(efd);
      else if (failItem is SimVarFailureDefinition svfd)
        ret = CreateSimVar(svfd);
      else if (failItem is StuckFailureDefinition sfd)
        ret = CreateStuck(sfd);
      else if (failItem is LeakFailureDefinition lfd)
        ret = CreateLeak(lfd);
      else
        throw new NotImplementedException();
      return ret;
    }

    private static FailureSustainer CreateSimVar(SimVarFailureDefinition svfd)
    {
      SimVarFailureSustainer ret = new SimVarFailureSustainer(svfd);
      return ret;
    }

    private static FailureSustainer CreateLeak(LeakFailureDefinition lfd)
    {
      LeakFailureSustainer ret = new LeakFailureSustainer(lfd);
      return ret;
    }

    private static FailureSustainer CreateStuck(StuckFailureDefinition sfd)
    {
      StuckFailureSustainer ret = new StuckFailureSustainer(sfd);
      return ret;
    }

    private static FailureSustainer CreateEvent(EventFailureDefinition ifd)
    {
      EventFailureSustainer ret = new EventFailureSustainer(ifd);
      return ret;
    }
  }
}