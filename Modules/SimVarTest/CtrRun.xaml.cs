﻿using Eng.Chlaot.Modules.SimVarTestModule.Controls;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace Eng.Chlaot.Modules.SimVarTestModule
{
  /// <summary>
  /// Interaction logic for CtrRun.xaml
  /// </summary>
  public partial class CtrRun : UserControl
  {
    private readonly Context context;

    public CtrRun()
    {
      InitializeComponent();
    }

    public CtrRun(Context context) : this()
    {
      this.context = context;
      this.DataContext = context;

      this.context.Connect();
    }

    private void btnNewSimVar_Click(object sender, RoutedEventArgs e)
    {
      string name = txtNewSimVar.Text;
      txtNewSimVar.Text = "";

      bool validate = chkNewSimVarValidate.IsChecked == true;

      this.context.RegisterNewSimVar(name, validate);
    }

    private void NewValue_NewValueRequested(NewValue sender, double newValue)
    {
      SimVarCase simVarCase = (SimVarCase)sender.Tag;

      this.context.SetValue(simVarCase, newValue);
    }
  }
}
