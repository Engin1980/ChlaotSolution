﻿using ELogging;
using Eng.Chlaot.ChlaotModuleBase;
using Eng.Chlaot.ChlaotModuleBase.ModuleUtils.KeyHooking;
using Eng.Chlaot.ChlaotModuleBase.ModuleUtils.Playing;
using Eng.Chlaot.ChlaotModuleBase.ModuleUtils.StateChecking;
using Eng.Chlaot.Modules.ChecklistModule.Types;
using Eng.Chlaot.Modules.ChecklistModule.Types.RunViews;
using Microsoft.VisualBasic.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Printing;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using Eng.Chlaot.ChlaotModuleBase.ModuleUtils.SimConWrapping.PrdefinedTypes;
using Eng.Chlaot.ChlaotModuleBase.ModuleUtils.SimConWrapping;

namespace Eng.Chlaot.Modules.ChecklistModule
{
    public class RunContext : NotifyPropertyChangedBase
  {
    public class AutoplayChecklistEvaluator
    {
      private readonly StateCheckEvaluator evaluator;
      private readonly object lck = new();
      private readonly RunContext parent;
      private bool autoplaySuppressed = false;
      private CheckList? prevList = null;


      public SimData SimData => this.parent.simConWrapper.SimData;

      public AutoplayChecklistEvaluator(RunContext parent)
      {
        this.parent = parent;
        this.evaluator = new StateCheckEvaluator(parent.variableValues, parent.propertyValues);
      }

      public bool EvaluateIfShouldPlay(CheckList checkList)
      {
        if (this.SimData.IsSimPaused) return false;
        if (Monitor.TryEnter(lck) == false) return false;

        this.parent.logHandler.Invoke(LogLevel.VERBOSE, $"Evaluation started for {checkList.Id}");

        if (prevList != checkList)
        {
          this.evaluator.Reset();
          this.autoplaySuppressed = false;
          prevList = checkList;
        }

        bool ret;
        if (this.autoplaySuppressed)
          ret = false;
        else
          ret = checkList.MetaInfo?.When != null && this.evaluator.Evaluate(checkList.MetaInfo.When);

        this.parent.logHandler.Invoke(LogLevel.VERBOSE,
          $"Evaluation finished for {checkList.Id} as={ret}, autoplaySupressed={autoplaySuppressed}");
        Monitor.Exit(lck);
        return ret;
      }

      internal void SuppressAutoplayForCurrentChecklist()
      {
        this.autoplaySuppressed = true;
      }
    }
    public class PlaybackManager
    {
      private readonly RunContext parent;
      private int currentItemIndex = 0;
      private CheckListView currentList;
      private bool isCallPlayed = false;
      private bool isEntryPlayed = false;
      private bool isPlaying = false;
      private CheckListView? previousList;
      public bool IsWaitingForNextChecklist { get => currentItemIndex == 0 && isEntryPlayed == false; }

      public PlaybackManager(RunContext parent)
      {
        this.parent = parent ?? throw new ArgumentNullException(nameof(parent));
        this.currentList = parent.CheckListViews.First();
      }

      public CheckList GetCurrentChecklist() => currentList.CheckList;

      public void PauseAsync()
      {
        this.isPlaying = false;
        this.isCallPlayed = false;
        this.isEntryPlayed = false;
      }

      public void Play()
      {
        lock (this)
        {
          this.isPlaying = true;
          byte[] playData = ResolveAndMarkNexPlayBytes(out bool stopPlaying);
          Player player = new(playData);
          player.PlaybackFinished += Player_PlaybackFinished;
          player.PlayAsync();
          if (stopPlaying) this.isPlaying = false;
        }
        AdjustRunStates();
      }

      public void Reset()
      {
        this.currentList = parent.CheckListViews.First();
        this.currentItemIndex = 0;
        this.AdjustRunStates();
      }

      public void TogglePlay()
      {
        if (isPlaying)
          PauseAsync();
        else
          Play();
      }

      internal void SkipToNext()
      {
        currentList = parent.CheckListViews.First(q => q.CheckList == currentList.CheckList.NextChecklist);
        currentItemIndex = 0;
        isEntryPlayed = false;
        isCallPlayed = false;
        AdjustRunStates();
        if (!isPlaying) this.Play();
      }

      internal void SkipToPrev()
      {
        if (isEntryPlayed || currentItemIndex > 0)
        {
          currentItemIndex = 0;
          isCallPlayed = false;
          isEntryPlayed = false;
        }
        else if (previousList != null)
        {
          currentList = previousList;
          previousList = null;
        }
        AdjustRunStates();
        if (!isPlaying) this.Play();
      }

      private void AdjustRunStates()
      {
        parent.CheckListViews
          .Where(q => q.State == RunState.Current && q != currentList)
          .ToList()
          .ForEach(q => q.State = RunState.Runned);
        for (int i = 0; i < currentList.Items.Count; i++)
        {
          if (i < currentItemIndex)
            currentList.Items[i].State = RunState.Runned;
          else if (i > currentItemIndex)
            currentList.Items[i].State = RunState.NotYet;
          else
            currentList.Items[i].State = RunState.Current;
        }
        if (currentList.State != RunState.Current)
          currentList.State = RunState.Current;
        if (currentItemIndex < currentList.Items.Count)
          currentList.Items[currentItemIndex].State = RunState.Current;
      }
      private void Player_PlaybackFinished(Player sender)
      {
        lock (this)
        {
          if (!this.isPlaying) return;
          this.Play();
        }
      }

      private byte[] ResolveAndMarkNexPlayBytes(out bool stopPlaying)
      {
        byte[] ret;
        if (currentItemIndex == 0 && !this.isEntryPlayed)
        {
          // playing checklist entry speech
          ret = currentList.CheckList.EntrySpeechBytes;
          this.isEntryPlayed = true;
          stopPlaying = false;
        }
        else if (currentItemIndex < currentList.Items.Count)
        {
          // play checklist item and increase index
          if (isCallPlayed == false)
          {
            ret = currentList.Items[currentItemIndex].CheckItem.Call.Bytes;
            isCallPlayed = true;

            if (!this.parent.settings.ReadConfirmations)
            {
              currentItemIndex++;
              isCallPlayed = false;
            }
          }
          else
          {
            ret = currentList.Items[currentItemIndex].CheckItem.Confirmation.Bytes;
            currentItemIndex++;
            isCallPlayed = false;
          }
          stopPlaying = false;
        }
        else
        {
          // playing at the end
          ret = currentList.CheckList.ExitSpeechBytes;
          this.isEntryPlayed = false;
          previousList = currentList;
          currentList = parent.CheckListViews.First(q => q.CheckList == currentList.CheckList.NextChecklist);
          currentItemIndex = 0;
          isCallPlayed = false;
          stopPlaying = true;
        }
        return ret;
      }
    }

    private readonly AutoplayChecklistEvaluator autoplayEvaluator;

    private readonly NewLogHandler logHandler;

    private readonly PlaybackManager playback;

    private readonly Settings settings;

    private readonly SimConWrapperWithSimData simConWrapper;

    private int keyHookPlayPauseId = -1;
    private int keyHookSkipNextId = -1;
    private int keyHookSkipPrevId = -1;

    private readonly Dictionary<string, double> propertyValues = new();
    private readonly Dictionary<string, double> variableValues = new();

    private KeyHookWrapper? keyHookWrapper;

    public List<CheckListView> CheckListViews
    {
      get => base.GetProperty<List<CheckListView>>(nameof(CheckListViews))!;
      set => base.UpdateProperty(nameof(CheckListViews), value);
    }

    public CheckSet CheckSet
    {
      get => base.GetProperty<CheckSet>(nameof(CheckSet))!;
      set => base.UpdateProperty(nameof(CheckSet), value);
    }

    public SimData SimData => this.simConWrapper.SimData;

    public RunContext(InitContext initContext)
    {
      this.CheckSet = initContext.ChecklistSet;
      this.settings = initContext.Settings;
      this.logHandler = Logger.RegisterSender(this, "[CheckList.RunContext]");

      this.CheckListViews = CheckSet.Checklists
        .Select(q => new CheckListView()
        {
          CheckList = q,
          State = RunState.NotYet,
          Items = q.Items
            .Select(p => new CheckItemView()
            {
              CheckItem = p,
              State = RunState.NotYet
            })
            .ToList()
        })
        .ToList();

      this.playback = new PlaybackManager(this);
      this.simConWrapper= new SimConWrapperWithSimData(new ESimConnect.ESimConnect());
      this.simConWrapper.SimSecondElapsed += Sim_SimSecondElapsed;
      this.autoplayEvaluator = new AutoplayChecklistEvaluator(this);
    }

    internal void Run(KeyHookWrapper keyHookWrapper)
    {
      logHandler?.Invoke(LogLevel.INFO, "Run");

      logHandler?.Invoke(LogLevel.VERBOSE, "Resetting playback");
      playback.Reset();

      logHandler?.Invoke(LogLevel.VERBOSE, "Adding key hooks");
      this.keyHookWrapper = keyHookWrapper ?? throw new ArgumentNullException(nameof(keyHookWrapper));
      ConnectKeyHooks();

      logHandler?.Invoke(LogLevel.VERBOSE, "Starting connection timer");

      this.simConWrapper.OpenAsync(
        () => this.simConWrapper.Start(),
        ex => this.Log(LogLevel.WARNING, "Failed to connect to FS2020, will try it again..."));

      logHandler?.Invoke(LogLevel.VERBOSE, "Run done");
    }

    internal void Stop()
    {
      throw new NotImplementedException();
    }

    private static KeyHookInfo ConvertShortcutToKeyHookInfo(Settings.KeyShortcut shortcut)
    {
      KeyHookInfo ret = new(
        shortcut.Alt,
        shortcut.Control,
        shortcut.Shift,
        shortcut.Key);
      return ret;
    }

    private void ConnectKeyHooks()
    {
      Settings.KeyShortcut s = null!;

      if (keyHookWrapper == null) throw new ApplicationException("KeyHookWrapper not set.");

      try
      {
        s = settings.Shortcuts.PlayPause;
        logHandler?.Invoke(LogLevel.INFO, "Assigning play-pause keyboard shortcut " + s);
        this.keyHookPlayPauseId = this.keyHookWrapper.RegisterKeyHook(ConvertShortcutToKeyHookInfo(s));
      }
      catch (Exception ex)
      {
        logHandler?.Invoke(LogLevel.ERROR, $"Failed to bind key-hook for shortcut {s}. Reason: {ex.GetFullMessage()}");
      }

      try
      {
        s = settings.Shortcuts.SkipToNext;
        logHandler?.Invoke(LogLevel.INFO, "Assigning skip-to-next keyboard shortcut " + s);
        this.keyHookSkipNextId = this.keyHookWrapper.RegisterKeyHook(ConvertShortcutToKeyHookInfo(s));
      }
      catch (Exception ex)
      {
        logHandler?.Invoke(LogLevel.ERROR, $"Failed to bind key-hook for shortcut {s}. Reason: {ex.GetFullMessage()}");
      }

      try
      {
        s = settings.Shortcuts.SkipToPrevious;
        logHandler?.Invoke(LogLevel.INFO, "Assigning skip-to-previous keyboard shortcut " + s);
        this.keyHookSkipPrevId = this.keyHookWrapper.RegisterKeyHook(ConvertShortcutToKeyHookInfo(s));
      }
      catch (Exception ex)
      {
        logHandler?.Invoke(LogLevel.ERROR, $"Failed to bind key-hook for shortcut {s}. Reason: {ex.GetFullMessage()}");
      }

      this.keyHookWrapper.KeyHookInvoked += keyHookWrapper_KeyHookInvoked;
    }

    private void keyHookWrapper_KeyHookInvoked(int hookId, KeyHookInfo keyHookInfo)
    {
      if (hookId == this.keyHookPlayPauseId)
      {
        this.playback.TogglePlay();
      }
      else if (hookId == this.keyHookSkipNextId)
      {
        this.playback.SkipToNext();
      }
      else if (hookId == this.keyHookSkipPrevId)
      {
        this.playback.SkipToPrev();
      }
      else
      {
        throw new ApplicationException($"Unknown hook id '{hookId}'.");
      }
    }

    private void Log(LogLevel level, string message)
    {
      logHandler.Invoke(level, message);
    }

    private void Sim_SimSecondElapsed()
    {
      if (playback.IsWaitingForNextChecklist == false) return;
      CheckList checkList = playback.GetCurrentChecklist();
      UpdatePropertyValues();
      bool shouldPlay = this.settings.UseAutoplay
        && this.autoplayEvaluator.EvaluateIfShouldPlay(checkList);
      if (shouldPlay)
      {
        autoplayEvaluator.SuppressAutoplayForCurrentChecklist();
        this.playback.TogglePlay();
      }
    }

    private void UpdatePropertyValues()
    {
      StateCheckEvaluator.UpdateDictionaryByObject(SimData, propertyValues);
    }
  }
}
