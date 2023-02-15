﻿using ChecklistModule.Support;
using ChecklistModule.Types;
using ChecklistModule.Types.Autostarts;
using ChlaotModuleBase;
using Eng.Chlaot.ChlaotModuleBase;
using EXmlLib;
using EXmlLib.Deserializers;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using System.Xml.Serialization;

namespace ChecklistModule
{
  public class Context : NotifyPropertyChangedBase, Eng.Chlaot.ChlaotModuleBase.IModuleProcessor
  {
    public delegate void LogDelegate(LogLevel level, string messaga);
    public event LogDelegate Log;
    public Settings Settings { get; } = new Settings();

    public CheckSet ChecklistSet
    {
      get => base.GetProperty<CheckSet>(nameof(ChecklistSet))!;
      set => base.UpdateProperty(nameof(ChecklistSet), value);
    }

    internal void LoadFile(string xmlFile)
    {
      CheckSet tmp;
      var factory = new XmlSerializerFactory();
      XDocument doc;

      try
      {
        doc = XDocument.Load(xmlFile);
        EXml<CheckSet> exml = CreateDeserializer();
        tmp = exml.Deserialize(doc);
      }
      catch (Exception ex)
      {
        throw ex;
        // this.DoLog(LogLevel.ERROR, "Unable to read checklist-set from '{xmlFile}'.", ex);
      }

      try
      {
        CheckSanity(tmp);
      }
      catch (Exception ex)
      {
        throw new ApplicationException("Error loading checklist.", ex);
      }

      try
      {
        BindNextChecklists(tmp);
      }
      catch (Exception ex)
      {
        throw new ApplicationException("Error binding checklist references.", ex);
      }

      try
      {
        InitializeSoundStreams(tmp);
      }
      catch (Exception ex)
      {
        throw new ApplicationException("Error creating sound streams for checklist.", ex);
      }

      this.ChecklistSet = tmp;
    }

    private void CheckSanity(CheckSet tmp)
    {
      // check no duplicit
      var ids = tmp.Checklists.Select(q => q.Id);
      var dids = ids.Distinct();
      var exc = ids.Except(dids);
      if (exc.Any())
      {
        throw new ApplicationException("There are repeated checklist id definitions: " + string.Join(", ", exc));
      }
    }

    private void InitializeSoundStreams(CheckSet checkSet)
    {
      Synthetizer synthetizer = new(Settings.Synthetizer.Voice, Settings.Synthetizer.Rate); //Synthetizer.CreateDefault();
      Dictionary<string, byte[]> generatedSounds = new();
      foreach (var checklist in checkSet.Checklists)
      {
        // TODO correct load meta data and checklist entry/exit speeches
        InitializeSoundStreamsForChecklist(checklist, generatedSounds, synthetizer);

        foreach (var item in checklist.Items)
        {
          InitializeSoundStreamsForItems(item.Call, generatedSounds, synthetizer);
          InitializeSoundStreamsForItems(item.Confirmation, generatedSounds, synthetizer);
        }
      }
    }

    private void InitializeSoundStreamsForChecklist(
      CheckList checklist,
      Dictionary<string, byte[]> generatedSounds,
      Synthetizer synthetizer)
    {
      if (checklist.MetaInfo?.CustomEntryCallout != null)
        InitializeSoundStreamsForItems(checklist.MetaInfo.CustomEntryCallout, generatedSounds, synthetizer);
      if (checklist.MetaInfo?.CustomExitCallout != null)
        InitializeSoundStreamsForItems(checklist.MetaInfo.CustomExitCallout, generatedSounds, synthetizer);

      checklist.EntrySpeechBytes =
        checklist.MetaInfo?.CustomEntryCallout != null
        ? checklist.MetaInfo.CustomEntryCallout.Bytes
        : synthetizer.Generate($"{checklist.CallSpeech} checklist");
      checklist.ExitSpeechBytes =
        checklist.MetaInfo?.CustomExitCallout != null
        ? checklist.MetaInfo.CustomExitCallout.Bytes
        : synthetizer.Generate($"{checklist.CallSpeech} checklist completed");
    }

    private void InitializeSoundStreamsForItems(
      CheckDefinition checkDefinition,
      Dictionary<string, byte[]> generatedSounds,
      Synthetizer synthetizer)
    {
      if (checkDefinition.Type == CheckDefinition.CheckDefinitionType.File)
        try
        {
          checkDefinition.Bytes = System.IO.File.ReadAllBytes(checkDefinition.Value);
        }
        catch (Exception ex)
        {
          throw new EXmlException($"Unable to load sound file '{checkDefinition.Value}'.", ex);
        }
      else if (checkDefinition.Type == CheckDefinition.CheckDefinitionType.Speech)
        try
        {
          if (generatedSounds.ContainsKey(checkDefinition.Value))
            checkDefinition.Bytes = generatedSounds[checkDefinition.Value];
          else
          {
            checkDefinition.Bytes = synthetizer.Generate(checkDefinition.Value);
            generatedSounds[checkDefinition.Value] = checkDefinition.Bytes;
          }
        }
        catch (Exception ex)
        {
          throw new EXmlException($"Unable to generated sound for speech '{checkDefinition.Value}'.", ex);
        }
    }

    private void BindNextChecklists(CheckSet tmp)
    {
      for (int i = 0; i < tmp.Checklists.Count; i++)
      {
        var checklist = tmp.Checklists[i];
        if (checklist.NextChecklistId is null)
        {
          if (i < tmp.Checklists.Count - 1) checklist.NextChecklist = tmp.Checklists[i + 1];
        }
        else
          checklist.NextChecklist = tmp.Checklists.Single(q => q.Id == checklist.NextChecklistId);
      }
    }

    private EXml<CheckSet> CreateDeserializer()
    {
      EXml<CheckSet> ret = new EXml<CheckSet>();

      var oed = new ObjectElementDeserializer()
        .WithCustomTargetType(typeof(CheckDefinition))
        .WithIgnoredProperty(nameof(CheckDefinition.Bytes));
      ret.Context.ElementDeserializers.Insert(0, oed);

      oed = new ObjectElementDeserializer()
        .WithCustomTargetType(typeof(CheckSet))
        .WithCustomPropertyDeserialization(
          nameof(CheckSet.Checklists),
          (e, t, f, c) =>
          {
            var deser = c.ResolveElementDeserializer(typeof(List<CheckList>));
            var items = e.LElements("checklist")
              .Select(q => SafeUtils.Deserialize(q, typeof(CheckList), deser, c))
              .Cast<CheckList>()
              .ToList();
            SafeUtils.SetPropertyValue(f, t, items);
          });
      ret.Context.ElementDeserializers.Insert(0, oed);

      oed = new ObjectElementDeserializer()
        .WithCustomTargetType(typeof(CheckList))
        .WithIgnoredProperty(nameof(CheckList.EntrySpeechBytes))
        .WithIgnoredProperty(nameof(CheckList.ExitSpeechBytes))
        .WithCustomPropertyDeserialization(
        nameof(CheckList.Items),
        (e, t, p, c) =>
        {
          var deser = c.ResolveElementDeserializer(typeof(List<CheckItem>));
          var items = e.LElements("item")
          .Select(q => SafeUtils.Deserialize(q, typeof(CheckItem), deser, c))
          .Cast<CheckItem>()
          .ToList();
          SafeUtils.SetPropertyValue(p, t, items);
        })
        .WithCustomPropertyDeserialization(
        nameof(CheckList.NextChecklistId),
        (e, t, p, c) =>
        {
          string? val = e.LElementOrNull("nextChecklistId")?.Attribute("id")?.Value;
          SafeUtils.SetPropertyValue(p, t, val);
        });
      ret.Context.ElementDeserializers.Insert(0, oed);

      oed = new ObjectElementDeserializer()
        .WithCustomTargetType(typeof(CheckSet))
      .WithCustomPropertyDeserialization(
        "Checklists",
        (e, t, f, c) =>
        {
          var deser = c.ResolveElementDeserializer(typeof(CheckList));
          var val = e.LElements("checklist")
          .Select(q => SafeUtils.Deserialize(q, typeof(CheckList), deser, c))
          .Cast<CheckList>()
          .ToList();
          SafeUtils.SetPropertyValue(f, t, val);
        });
      ret.Context.ElementDeserializers.Insert(0, oed);

      ret.Context.ElementDeserializers.Insert(0, new AutostartDeserializer());

      return ret;
    }

    private void DoLog(LogLevel level, string message, Exception? cause = null)
    {
      StringBuilder sb = new(" ");
      while (cause != null)
      {
        sb = sb.Append(cause.ToString());
        cause = cause.InnerException;
      }
      this.Log?.Invoke(level, message + sb.ToString());
    }
  }
}
