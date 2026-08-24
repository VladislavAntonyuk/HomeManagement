using System.Globalization;

namespace HomeManagement.Infrastructure.Database.Entities;

public class AppSetting
{
    public int Id { get; init; }

    public required string Name { get; init; }

    public required string Value { get; set; }

    public AppSettingValueType ValueType { get; set; }

    public void SetBoolean(bool value)
    {
        ValueType = AppSettingValueType.Boolean;
        Value = value ? bool.TrueString : bool.FalseString;
    }

    public bool TryGetBoolean(out bool value)
    {
        if (ValueType != AppSettingValueType.Boolean)
        {
            value = false;
            return false;
        }

        return bool.TryParse(Value, out value);
    }

    public void SetInteger(int value)
    {
        ValueType = AppSettingValueType.Integer;
        Value = value.ToString(CultureInfo.InvariantCulture);
    }

    public bool TryGetInteger(out int value)
    {
        if (ValueType != AppSettingValueType.Integer)
        {
            value = 0;
            return false;
        }

        return int.TryParse(Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    public void SetString(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        ValueType = AppSettingValueType.String;
        Value = value;
    }

    public bool TryGetString(out string value)
    {
        if (ValueType != AppSettingValueType.String)
        {
            value = string.Empty;
            return false;
        }

        value = Value;
        return true;
    }
}

public enum AppSettingValueType
{
    Boolean,
    Integer,
    String
}
