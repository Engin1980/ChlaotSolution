﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Timers;
using FailuresModule.Model.Run.Sustainers;
using FailuresModule.Model.Failures;

namespace FailuresModule.Model.Run.Sustainers
{
  internal class StuckFailureSustainer : SimVarBasedFailureSustainer
  {
    #region Fields

    private bool isRunning = false;
    private readonly Timer updateTimer;
    private StuckFailureDefinition failure;

    #endregion Fields

    #region Properties

    public double? StuckValue
    {
      get => base.GetProperty<double?>(nameof(StuckValue))!;
      set => base.UpdateProperty(nameof(StuckValue), value);
    }

    #endregion Properties

    #region Constructors

    public StuckFailureSustainer(StuckFailureDefinition failure) : base(failure)
    {
      this.failure = failure;
      this.updateTimer = new Timer(this.failure.RefreshIntervalInMs);
      this.updateTimer.Elapsed += UpdateTimer_Elapsed;
      base.DataReceived += StuckFailureSustainer_DataReceived;
      //TODO is not using "onlyWhenChanged" flag
    }

    #endregion Constructors

    #region Methods

    protected override void InitInternal()
    {
      base.InitInternal();
    }

    protected override void ResetInternal()
    {
      lock (this)
      {
        this.updateTimer.Enabled = false;
        this.isRunning = false;
        this.StuckValue = null;
      }
    }

    protected override void StartInternal()
    {
      this.isRunning = true;
      RequestData();
    }

    private void StuckFailureSustainer_DataReceived(double data)
    {
      if (this.StuckValue == null && isRunning)
      {
        lock (this)
        {
          if (this.StuckValue == null)
          {
            this.StuckValue = data;
            updateTimer.Start();
          }
        }
      }
    }
    private void UpdateTimer_Elapsed(object? sender, ElapsedEventArgs e)
    {
      lock (this)
      {
        Debug.Assert(this.StuckValue != null);
        base.SendData(this.StuckValue.Value);
      }
    }

    #endregion Methods
  }
}