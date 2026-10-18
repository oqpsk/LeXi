using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Lexi.Core;

namespace Lexi;

public sealed partial class SettingsWindow
{
    private sealed class InferencePicker : StackPanel
    {
        private readonly ComboBox effort = new(), speed = new();
        private readonly TextBlock note = Text("", 13, Muted);
        private readonly TextBlock effortLabel = Text("Effort · 思考深度", 13, Muted), speedLabel = Text("速度", 13, Muted);
        private bool updating;
        private ModelChoice? model;
        private readonly AppSettings draft;
        public event Action? Changed;
        public InferencePicker(AppSettings draft, string name)
        {
            this.draft = draft; Margin = new(0,0,0,14);
            var columns = new Grid(); columns.ColumnDefinitions.Add(new()); columns.ColumnDefinitions.Add(new());
            var left = new StackPanel { Margin = new(0,0,7,0) }; left.Children.Add(effortLabel); left.Children.Add(effort);
            var right = new StackPanel { Margin = new(7,0,0,0) }; right.Children.Add(speedLabel); right.Children.Add(speed);
            Grid.SetColumn(right, 1); columns.Children.Add(left); columns.Children.Add(right); Children.Add(columns);
            note.Margin = new(0,6,0,0); Children.Add(note);
            AutomationProperties.SetName(effort, name + " effort"); AutomationProperties.SetName(speed, name + "速度");
            effort.SelectionChanged += (_, _) => Select(); speed.SelectionChanged += (_, _) => Select();
        }
        private void Select()
        {
            if (updating || model == null || effort.SelectedItem is not InferenceOption e || speed.SelectedItem is not InferenceOption s) return;
            try { InferenceOptions.Configure(draft, model, e.Id, s.Id); Changed?.Invoke(); }
            catch (InvalidOperationException ex) { note.Text = ex.Message; }
        }
        public void Refresh(ModelChoice? choice)
        {
            updating = true;
            try
            {
                model = choice; if (choice == null) { IsEnabled = false; return; } IsEnabled = true;
                var selected = InferenceOptions.Selected(draft, new(choice.Provider, choice.Id));
                void Set(ComboBox picker, InferenceOption[] choices, string value)
                {
                    var values = choices.ToList();
                    if (!values.Any(c => c.Id == value)) values.Add(new(value, value + "（当前不可用）"));
                    picker.ItemsSource = values; picker.SelectedItem = values.First(c => c.Id == value);
                    picker.IsEnabled = values.Count > 1;
                }
                Set(effort, choice.Capabilities.EffortChoices, selected.Effort); Set(speed, choice.Capabilities.SpeedChoices, selected.Speed);
                effortLabel.Opacity = effort.IsEnabled ? 1 : 0.45; speedLabel.Opacity = speed.IsEnabled ? 1 : 0.45;
                ToolTipService.SetShowOnDisabled(effort, true); ToolTipService.SetShowOnDisabled(speed, true);
                effort.ToolTip = effort.IsEnabled ? null : "此模型未提供可选 effort，使用模型默认设置。";
                speed.ToolTip = speed.IsEnabled ? null : "此模型未提供可选速度，使用服务默认设置。";
                note.Text = choice.Capabilities.Note;
            }
            finally { updating = false; }
        }
    }
}
