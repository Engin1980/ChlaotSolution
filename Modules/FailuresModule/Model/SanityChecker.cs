﻿using ELogging;
using Eng.Chlaot.ChlaotModuleBase.ModuleUtils.StateChecking;
using FailuresModule.Model.App;
using FailuresModule.Model.Sim;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace FailuresModule.Types
{
    internal class SanityChecker
  {
    private readonly Stack<string> context = new();
    private List<FailureDefinition> failureDefinitions;

    internal static void CheckSanity(IncidentTopGroup tmp, List<FailureDefinition> failureDefinitions)
    {
      SanityChecker sc = new()
      {
        failureDefinitions = failureDefinitions
      };
      sc.CheckSanityInternal(tmp);
    }

    private void CheckSanityInternal(IncidentGroup incidentGroup)
    {
      Logger.Log(this, LogLevel.VERBOSE, "Checking sanity of incidentGroup");
      WithContext($"{incidentGroup.Title} (IncidentGroup)", () => CheckSanityInternal(incidentGroup.Incidents));
    }

    private void CheckSanityInternal(List<Incident> incidents)
    {
      Logger.Log(this, LogLevel.VERBOSE, "Checking sanity of incidents");
      foreach (Incident incident in incidents)
      {
        if (incident is IncidentGroup incidentGroup)
        {
          CheckSanityInternal(incidentGroup);
        }
        else if (incident is IncidentDefinition incidentDefinition)
        {
          CheckSanityInternal(incidentDefinition);
        }
        else
          throw new NotImplementedException();
      }
    }

    private void CheckSanityInternal(IncidentDefinition incidentDefinition)
    {
      Logger.Log(this, LogLevel.VERBOSE, "Checking sanity of incidentDefinition");
      context.Push($"{incidentDefinition.Title} (IncidentDefinition)");
      WithContext("Variables", () => CheckSanityInternal(incidentDefinition.Variables));
      WithContext("Triggers", () => CheckSanityInternal(incidentDefinition.Triggers));
      AssertNotNull(incidentDefinition.FailGroup, nameof(incidentDefinition.FailGroup));
      WithContext("FailGroup", () => CheckSanityInternal(incidentDefinition.FailGroup));
      context.Pop();
    }

    private void CheckSanityInternal(List<Trigger> triggers)
    {
      Logger.Log(this, LogLevel.VERBOSE, "Checking sanity of triggers");
      foreach (var trigger in triggers)
      {
        //TODO thle se mi nechtlěo psát zatím
      }
    }

    private void WithContext(string context, Action action)
    {
      this.context.Push(context);
      action();
      this.context.Pop();
    }

    private void CheckSanityInternal(List<Variable> variables)
    {
      Logger.Log(this, LogLevel.VERBOSE, "Checking sanity of variables");
      foreach (var variable in variables)
      {
        AssertTrue(string.IsNullOrWhiteSpace(variable.Name) == false, "Variable name is null or whitespace.");
        if (variable is RandomVariable randomVariable)
        {
          AssertTrue(
            randomVariable.Minimum < randomVariable.Maximum,
            $"Random-Variable {randomVariable.Name} minimum must be below maximum (provided minimum={randomVariable.Minimum}, maximum={randomVariable.Maximum}.");
        }
        else if (variable is UserVariable userVariable)
        {
          // intentionally blank
        }
        else
        {
          throw new NotImplementedException();
        }
      }
    }

    private void CheckSanityInternal(Fail failItem)
    {
      Logger.Log(this, LogLevel.VERBOSE, "Checking sanity of failItem");
      AssertTrue(failItem.Weight >= 0, $"Weight must be >=0 (provided={failItem.Weight})");
      if (failItem is FailId failure)
      {
        if (failureDefinitions.Any(q => q.Id == failure.Id) == false)
          throw new ApplicationException($"Failure id {failure.Id} not found among failures.");
      }
      else if (failItem is FailGroup failureGroup)
      {
        WithContext("FailGroup", () => failureGroup.Items.ForEach(q => CheckSanityInternal(q)));
      }
    }

    private void CheckSanityInternal(IncidentTopGroup failureSet)
    {
      Logger.Log(this, LogLevel.VERBOSE, "Checking sanity of failureSet");
      context.Push($"Failure-set '{failureSet.MetaInfo.Label}'");
      CheckSanityInternal(failureSet.Incidents);
      context.Pop();
    }

    private void AssertTrue(bool condition, string errMessage)
    {
      if (!condition)
      {
        string contextString = string.Join("->", context.ToList());
        throw new ApplicationException($"Sanity check failed: {contextString} :: {errMessage}");
      }
    }

    private void AssertNotNull(object value, string name)
    {
      AssertTrue(value != null, $"{name} is null.");
    }
  }
}