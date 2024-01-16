﻿using CopilotModule;
using ELogging;
using Eng.Chlaot.ChlaotModuleBase;
using Eng.Chlaot.ChlaotModuleBase.ModuleUtils;
using Eng.Chlaot.ChlaotModuleBase.ModuleUtils.SimObjects;
using Eng.Chlaot.ChlaotModuleBase.ModuleUtils.StateChecking;
using Eng.Chlaot.ChlaotModuleBase.ModuleUtils.StateChecking.StateModel;
using Eng.Chlaot.ChlaotModuleBase.ModuleUtils.StateChecking.VariableModel;
using Eng.Chlaot.ChlaotModuleBase.ModuleUtils.Synthetization;
using Eng.Chlaot.Modules.CopilotModule.Types;
using EXmlLib;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using static ESystem.Functions;
using System.Xml.Linq;
using System.Xml.Serialization;
using ChlaotModuleBase.ModuleUtils.StateChecking;
using ESystem;
using static ChlaotModuleBase.ModuleUtils.StateChecking.StateCheckUtils;

namespace Eng.Chlaot.Modules.CopilotModule
{
  public class InitContext : NotifyPropertyChangedBase
  {
    private readonly Logger logger;
    private readonly Action<bool> setIsReadyFlagAction;
    private readonly Dictionary<Variable, SpeechDefinition> variableToSpeechDefinitionMapping = new();

    public CopilotSet Set
    {
      get => base.GetProperty<CopilotSet>(nameof(Set))!;
      set => base.UpdateProperty(nameof(Set), value);
    }


    public MetaInfo MetaInfo
    {
      get => base.GetProperty<MetaInfo>(nameof(MetaInfo))!;
      set => base.UpdateProperty(nameof(MetaInfo), value);
    }

    internal Settings Settings { get; private set; }

    public SimPropertyGroup SimPropertyGroup
    {
      get => base.GetProperty<SimPropertyGroup>(nameof(SimPropertyGroup))!;
      set => base.UpdateProperty(nameof(SimPropertyGroup), value);
    }

    public record PropertyUsageCount(SimProperty Property, int Count);

    public List<PropertyUsageCount> PropertyUsageCounts
    {
      get => base.GetProperty<List<PropertyUsageCount>>(nameof(PropertyUsageCounts))!;
      set => base.UpdateProperty(nameof(PropertyUsageCounts), value);
    }

    internal InitContext(Settings settings, Action<bool> setIsReadyFlagAction)
    {
      Settings = settings ?? throw new ArgumentNullException(nameof(settings));
      this.logger = Logger.Create(this, "Copilot.InitContext");
      this.setIsReadyFlagAction = setIsReadyFlagAction ?? throw new ArgumentNullException(nameof(setIsReadyFlagAction));
      this.SimPropertyGroup = LoadDefaultSimProperties();
    }

    private SimPropertyGroup LoadDefaultSimProperties()
    {
      SimPropertyGroup ret;
      try
      {
        XDocument doc = XDocument.Load(@"Xmls\SimProperties.xml", LoadOptions.SetLineInfo);
        ret = SimPropertyGroup.Deserialize(doc.Root!);
      }
      catch (Exception ex)
      {
        throw new ApplicationException("Failed to load global sim properties.", ex);
      }
      return ret;
    }

    internal void LoadFile(string xmlFile)
    {
      MetaInfo tmpMeta;
      CopilotSet tmp;
      SimPropertyGroup? tmpSpg = null;

      try
      {
        logger.Invoke(LogLevel.INFO, $"Checking file '{xmlFile}'");
        try
        {
          XmlUtils.ValidateXmlAgainstXsd(xmlFile, new string[] { @".\xmls\xsds\Global.xsd", @".\xmls\xsds\CopilotSchema.xsd" }, out List<string> errors);
          if (errors.Any())
            throw new ApplicationException("XML does not match XSD: " + string.Join("; ", errors.Take(5)));
        }
        catch (Exception ex)
        {
          throw new ApplicationException($"Failed to validate XMl file against XSD. Error: " + ex.Message, ex);
        }

        logger.Invoke(LogLevel.INFO, $"Loading file '{xmlFile}'");
        try
        {
          XDocument doc = XDocument.Load(xmlFile, LoadOptions.SetLineInfo);
          tmpMeta = MetaInfo.Deserialize(doc);
          if (doc.Root!.LElement("properties") is XElement pelm)
            // workaround due to WPF binding refresh
            tmpSpg = SimPropertyGroup.Deserialize(pelm);
          tmp = Eng.Chlaot.CopilotModule.Types.Xml.Deserializer.Deserialize(doc);
        }
        catch (Exception ex)
        {
          throw new ApplicationException("Unable to read/deserialize copilot-set from '{xmlFile}'. Invalid file content?", ex);
        }

        logger.Invoke(LogLevel.INFO, $"Checking sanity");
        var props = tmpSpg == null
          ? this.SimPropertyGroup.GetAllSimPropertiesRecursively()
          : tmpSpg.GetAllSimPropertiesRecursively().Union(this.SimPropertyGroup.GetAllSimPropertiesRecursively()).ToList();
        Try(() => CheckSanity(tmp, props), ex => new ApplicationException("Error loading copilot data.", ex));

        logger.Invoke(LogLevel.INFO, $"Loading/generating sounds");
        Try(() => InitializeSoundStreams(tmp, System.IO.Path.GetDirectoryName(xmlFile)!),
          ex => new ApplicationException("Error creating sound streams.", ex));

        logger.Invoke(LogLevel.INFO, "Binding property changed events");
        BindPropertyChangedEvents(tmp);

        if (tmpSpg != null)
        {
          var spg = new SimPropertyGroup();
          spg.Properties.AddRange(this.SimPropertyGroup.Properties);
          spg.Properties.Add(tmpSpg);
          this.SimPropertyGroup = spg;
        }
        this.PropertyUsageCounts = GetPropertyUsagesCounts(tmp, this.SimPropertyGroup.GetAllSimPropertiesRecursively());
        this.MetaInfo = tmpMeta;
        this.Set = tmp;
        UpdateReadyFlag();
        logger.Invoke(LogLevel.INFO, $"Copilot set file '{xmlFile}' successfully loaded.");

      }
      catch (Exception ex)
      {
        this.setIsReadyFlagAction(false);
        logger.Invoke(LogLevel.ERROR, $"Failed to load copilot set from '{xmlFile}'." + ex.GetFullMessage());
      }
    }

    private void BindPropertyChangedEvents(CopilotSet tmp)
    {
      tmp.SpeechDefinitions.ForEach(q =>
      {
        q.Variables
          .Where(q => q is UserVariable)
          .Cast<UserVariable>()
          .ForEach(p =>
          {
            p.PropertyChanged += Variable_PropertyChanged;
            variableToSpeechDefinitionMapping[p] = q;
          });
      });
    }

    private List<PropertyUsageCount> GetPropertyUsagesCounts(CopilotSet tmp, List<SimProperty> simProperties)
    {
      Dictionary<string, int> dct = new();

      var lst = tmp.SpeechDefinitions
        .Select(q => q.Trigger)
        .Union(tmp.SpeechDefinitions
          .Where(q => q.ReactivationTrigger != null)
          .Select(q => q.ReactivationTrigger!));
      foreach (var item in lst)
      {
        var pus = StateCheckUtils.ExtractProperties(item);
        foreach (var pu in pus)
        {
          var pn = pu.PropertyName;
          if (dct.ContainsKey(pn))
            dct[pn]++;
          else
            dct[pn] = 1;
        }
      }

      List<PropertyUsageCount> ret = dct
        .Select(q => new PropertyUsageCount(simProperties.First(p => p.Name == q.Key), q.Value))
        .OrderBy(q => q.Property.Name)
        .ToList();
      return ret;
    }

    private void CheckSanity(CopilotSet tmp, List<SimProperty> definedProperties)
    {
      // all property conditions has values
      IStateCheckItem[] triggers = tmp.SpeechDefinitions.Select(q => q.Trigger).ToArray();
      IStateCheckItem[] reactivationTriggers = tmp.SpeechDefinitions.Where(q => q.ReactivationTrigger != null).Select(q => q.ReactivationTrigger!).ToArray();
      IStateCheckItem[] allTriggers = triggers.Union(reactivationTriggers).ToArray();
      List<StateCheckProperty> allTriggerProps = StateCheckUtils.ExtractStateCheckProperties(allTriggers);
      allTriggerProps
        .Where(q => q.Expression == null)
        .ForEach(q => throw new ApplicationException($"Expression of checked property {q.DisplayString} not set."));

      // check all properties defined
      List<PropertyUsage> propertyUsages = StateCheckUtils.ExtractProperties(allTriggers);
      List<string> missingProperties = propertyUsages
        .Where(q => definedProperties.None(p => p.Name == q.PropertyName))
        .Select(q => q.PropertyName)
        .Distinct()
        .ToList();
      if (missingProperties.Any())
        throw new ApplicationException($"Required properties not found in defined properties: {string.Join(", ", missingProperties)}");

      // all variables are defined
      // check all variables are defined
      List<string> missingVariables = new();
      foreach (var item in tmp.SpeechDefinitions)
      {
        List<Variable> definedVariables = item.Variables;
        List<VariableUsage> variableUsages = StateCheckUtils.ExtractVariables(item.Trigger);
        var vmp = variableUsages
          .Where(q => definedVariables.None(v => v.Name == q.VariableName))
          .Select(q => q.VariableName)
          .ToList();
        missingVariables.AddRange(vmp);
      }
      foreach (var item in tmp.SpeechDefinitions.Where(q => q.ReactivationTrigger != null))
      {
        List<Variable> definedVariables = item.Variables;
        List<VariableUsage> variableUsages = StateCheckUtils.ExtractVariables(item.ReactivationTrigger!);
        var vmp = variableUsages
          .Where(q => definedVariables.None(v => v.Name == q.VariableName))
          .Select(q => q.VariableName)
          .ToList();
        missingVariables.AddRange(vmp);
      }
      if (missingVariables.Any())
        throw new ApplicationException($"Required variables not found in defined variables: {string.Join(", ", missingVariables.Distinct())}.");
    }

    private void UpdateReadyFlag()
    {
      bool ready = this.Set != null && this.Set.SpeechDefinitions.SelectMany(q => q.Variables).All(q => !double.IsNaN(q.Value));
      this.setIsReadyFlagAction(ready);
    }

    private void Variable_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
      UserVariable variable = (UserVariable)sender!;
      SpeechDefinition sd = variableToSpeechDefinitionMapping[variable];
      if (sd.Speech.Type == Speech.SpeechType.Speech
        && sd.Speech.GetUsedVariables().Any(q => q == variable.Name))
      {
        BuildSpeech(sd, new(), new Synthetizer(this.Settings.Synthetizer), "");
      }

      UpdateReadyFlag();
    }

    private void InitializeSoundStreams(CopilotSet set, string relativePath)
    {
      Synthetizer synthetizer = new(Settings.Synthetizer);
      Dictionary<string, byte[]> generatedSounds = new();
      foreach (var sd in set.SpeechDefinitions)
      {
        BuildSpeech(sd, generatedSounds, synthetizer, relativePath);
      }
    }

    private void BuildSpeech(
      SpeechDefinition speechDefinition,
      Dictionary<string, byte[]> generatedSounds,
      Synthetizer synthetizer,
      string relativePath)
    {
      Speech speech = speechDefinition.Speech;
      if (speech.Type == Speech.SpeechType.File)
        try
        {
          speech.Bytes = System.IO.File.ReadAllBytes(
            System.IO.Path.Combine(relativePath, speech.Value));
        }
        catch (Exception ex)
        {
          throw new EXmlException($"Unable to load sound file '{speech.Value}'.", ex);
        }
      else if (speech.Type == Speech.SpeechType.Speech)
      {
        string txt = speech.GetEvaluatedValue(speechDefinition.Variables);
        try
        {
          if (generatedSounds.ContainsKey(txt))
            speech.Bytes = generatedSounds[txt];
          else
          {
            speech.Bytes = synthetizer.Generate(txt);
            generatedSounds[txt] = speech.Bytes;
          }
        }
        catch (Exception ex)
        {
          throw new EXmlException($"Unable to generated sound for speech '{speech.Value}'.", ex);
        }
      }
      else
        throw new NotImplementedException($"Unknown type {speech.Type}.");
    }
  }
}
