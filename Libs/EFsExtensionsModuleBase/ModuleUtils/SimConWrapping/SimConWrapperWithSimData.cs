﻿using Eng.EFsExtensions.EFsExtensionsModuleBase.ModuleUtils.SimConWrapping.PrdefinedTypes;
using ESimConnect;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Eng.EFsExtensions.EFsExtensionsModuleBase.ModuleUtils.SimConWrapping
{
  public class SimConWrapperWithSimData : SimConWrapperWithSimSecond
  {
    #region Public Delegates

    #endregion Public Delegates

    #region Public Events

    #endregion Public Events

    #region Private Fields

    public SimData SimData { get; } = new();

    #endregion Private Fields

    #region Public Constructors

    public SimConWrapperWithSimData(ESimConnect.ESimConnect simCon) : base(simCon)
    {
    }

    #endregion Public Constructors

    #region Public Methods

    #endregion Public Methods

    #region Protected Methods

    protected override void StartProtected()
    {
      base.StartProtected();

      base.simCon.Structs.Register<CommonDataStruct>();
      base.simCon.Structs.RequestRepeatedly<CommonDataStruct>(SimConnectPeriod.SECOND, sendOnlyOnChange: true);

      base.simCon.Structs.Register<RareDataStruct>();
      base.simCon.Structs.RequestRepeatedly<RareDataStruct>(SimConnectPeriod.SECOND, sendOnlyOnChange: true);

      base.simCon.DataReceived += SimCon_DataReceived;
    }

    #endregion Protected Methods

    #region Private Methods

    private void SimCon_DataReceived(ESimConnect.ESimConnect sender, ESimConnect.ESimConnect.ESimConnectDataReceivedEventArgs e)
    {
      if (e.Type == typeof(CommonDataStruct))
      {
        CommonDataStruct s = (CommonDataStruct)e.Data;
        SimData.Update(s);
      }
      else if (e.Type == typeof(RareDataStruct))
      {
        RareDataStruct s = (RareDataStruct)e.Data;
        SimData.Update(s);
      }
    }

    #endregion Private Methods
  }
}
