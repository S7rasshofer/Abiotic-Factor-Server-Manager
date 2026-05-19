using System.Globalization;

namespace AbioticServerManager.Core.Schema;

/// <summary>
/// Infers a setting's type from its live value when no metadata describes it. This is the
/// fallback that keeps unknown / future settings editable instead of silently dropped.
/// </summary>
public static class SettingTypeInference
{
    public static SettingValueType Infer(string value)
    {
        var trimmed = value.Trim();

        if (trimmed.Equals("true", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("false", StringComparison.OrdinalIgnoreCase))
        {
            return SettingValueType.Boolean;
        }

        if (long.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out _) ||
            double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
        {
            return SettingValueType.Number;
        }

        return SettingValueType.Text;
    }
}
