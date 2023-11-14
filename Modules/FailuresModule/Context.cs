﻿using ELogging;
using Eng.Chlaot.ChlaotModuleBase;
using EXmlLib;
using EXmlLib.Deserializers;
using FailuresModule.Types;
using FailuresModule.Xmls;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using System.Xml.Serialization;

namespace FailuresModule
{
    public class Context : NotifyPropertyChangedBase
  {
    private readonly NewLogHandler logHandler;
    private readonly Action<bool> setIsReadyFlagAction;

    public Context(NewLogHandler logHandler, Action<bool> setIsReadyFlagAction)
    {
      this.logHandler = logHandler ?? throw new ArgumentNullException(nameof(logHandler));
      this.setIsReadyFlagAction = setIsReadyFlagAction ?? throw new ArgumentNullException(nameof(setIsReadyFlagAction));
      this.FailureDefinitions = new();
      this.BuildFailures();
      //this.FailGroup = new("Root");
    }

    //public FailGroup FailGroup
    //{
    //  get => base.GetProperty<FailGroup>(nameof(FailGroup))!;
    //  set => base.UpdateProperty(nameof(FailGroup), value);
    //}

    public List<FailureDefinition> FailureDefinitions { get; set; }

    public FailureSet FailureSet
    {
      get => base.GetProperty<FailureSet>(nameof(FailureSet))!;
      set => base.UpdateProperty(nameof(FailureSet), value);
    }

    internal void BuildFailures()
    {
      this.FailureDefinitions = FailureDefinitionFactory.BuildFailures();
    }

    public void LoadFile(string xmlFile)
    {
      var factory = new XmlSerializerFactory();
      FailureSet tmp;
      XDocument doc;

      try
      {
        logHandler.Invoke(LogLevel.INFO, $"Loading file '{xmlFile}'");
        try
        {
          doc = XDocument.Load(xmlFile, LoadOptions.SetLineInfo);
          EXml<FailureSet> exml = Deserialization.CreateDeserializer(this.FailureDefinitions, this.logHandler);
          tmp = exml.Deserialize(doc);
        }
        catch (Exception ex)
        {
          throw new ApplicationException("Unable to read/deserialize copilot-set from '{xmlFile}'. Invalid file content?", ex);
        }

        logHandler.Invoke(LogLevel.INFO, $"Checking sanity");
        try
        {
          SanityChecker.CheckSanity(tmp, this.FailureDefinitions);
        }
        catch (Exception ex)
        {
          throw new ApplicationException("Error loading failures.", ex);
        }

        this.FailureSet = tmp;
        UpdateReadyFlag();
        logHandler.Invoke(LogLevel.INFO, $"Failure set file '{xmlFile}' successfully loaded.");

      }
      catch (Exception ex)
      {
        this.setIsReadyFlagAction(false);
        logHandler.Invoke(LogLevel.ERROR, $"Failed to load failure set from '{xmlFile}'." + ex.GetFullMessage());
      }
    }

    private void UpdateReadyFlag()
    {
      logHandler.Invoke(LogLevel.WARNING, "UpdateReadyFlag() NotImplemented");
    }
  }
}