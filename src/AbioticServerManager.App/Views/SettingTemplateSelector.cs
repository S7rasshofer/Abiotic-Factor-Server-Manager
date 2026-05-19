using System.Windows;
using System.Windows.Controls;
using AbioticServerManager.App.ViewModels;
using AbioticServerManager.Core.Schema;

namespace AbioticServerManager.App.Views;

/// <summary>
/// Picks the editor template for a setting from its inferred/declared control type.
/// This is the "generated WPF controls" step of the dynamic settings pipeline (plan §7.1).
/// </summary>
public sealed class SettingTemplateSelector : DataTemplateSelector
{
    public DataTemplate? ToggleTemplate { get; set; }
    public DataTemplate? SliderTemplate { get; set; }
    public DataTemplate? NumberTemplate { get; set; }
    public DataTemplate? DropdownTemplate { get; set; }
    public DataTemplate? TextTemplate { get; set; }

    public override DataTemplate? SelectTemplate(object? item, DependencyObject container) =>
        item is SettingViewModel svm
            ? svm.ControlType switch
            {
                SettingControlType.Toggle => ToggleTemplate,
                SettingControlType.Slider => SliderTemplate,
                SettingControlType.Number => NumberTemplate,
                SettingControlType.Dropdown => DropdownTemplate,
                _ => TextTemplate,
            }
            : base.SelectTemplate(item, container);
}
