using BaseLine.Core;
using Microsoft.Win32;

namespace BaseLine.Infrastructure;

public interface IRegistryAccessor
{
    RegistryDataSnapshot? ReadValue(RegistryRoot root, string path, string valueName);
    bool KeyExists(RegistryRoot root, string path);
    bool WriteValue(RegistryRoot root, string path, string valueName, RegistryDataSnapshot value);
    bool DeleteValue(RegistryRoot root, string path, string valueName);
}

public sealed class RegistryAccessor : IRegistryAccessor
{
    public RegistryDataSnapshot? ReadValue(RegistryRoot root, string path, string valueName)
    {
        try
        {
            using var baseKey = OpenBaseKey(root);
            using var key = baseKey.OpenSubKey(path, writable: false);
            if (key is null)
            {
                return null;
            }

            var raw = key.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            if (raw is null)
            {
                return null;
            }

            return FromRegistryValue(key.GetValueKind(valueName), raw);
        }
        catch
        {
            return null;
        }
    }

    public bool KeyExists(RegistryRoot root, string path)
    {
        try
        {
            using var baseKey = OpenBaseKey(root);
            using var key = baseKey.OpenSubKey(path, writable: false);
            return key is not null;
        }
        catch
        {
            return false;
        }
    }

    public bool WriteValue(RegistryRoot root, string path, string valueName, RegistryDataSnapshot value)
    {
        try
        {
            using var baseKey = OpenBaseKey(root);
            using var key = baseKey.CreateSubKey(path, writable: true);
            if (key is null)
            {
                return false;
            }

            switch (value.DataKind)
            {
                case RegistryDataKind.String:
                    key.SetValue(valueName, value.StringValue ?? string.Empty, RegistryValueKind.String);
                    break;
                case RegistryDataKind.ExpandString:
                    key.SetValue(valueName, value.StringValue ?? string.Empty, RegistryValueKind.ExpandString);
                    break;
                case RegistryDataKind.MultiString:
                    key.SetValue(valueName, value.MultiStringValue.ToArray(), RegistryValueKind.MultiString);
                    break;
                case RegistryDataKind.DWord:
                    key.SetValue(valueName, Convert.ToInt32(value.NumericValue ?? 0), RegistryValueKind.DWord);
                    break;
                case RegistryDataKind.QWord:
                    key.SetValue(valueName, value.NumericValue ?? 0, RegistryValueKind.QWord);
                    break;
                case RegistryDataKind.Binary:
                    key.SetValue(valueName, value.BinaryValue, RegistryValueKind.Binary);
                    break;
                default:
                    key.SetValue(valueName, value.StringValue ?? string.Empty, RegistryValueKind.String);
                    break;
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    public bool DeleteValue(RegistryRoot root, string path, string valueName)
    {
        try
        {
            using var baseKey = OpenBaseKey(root);
            using var key = baseKey.OpenSubKey(path, writable: true);
            if (key is null)
            {
                return false;
            }

            key.DeleteValue(valueName, throwOnMissingValue: false);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static RegistryKey OpenBaseKey(RegistryRoot root)
    {
        return root == RegistryRoot.CurrentUser
            ? RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Default)
            : RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Default);
    }

    public static RegistryDataSnapshot FromRegistryValue(RegistryValueKind kind, object raw)
    {
        return kind switch
        {
            RegistryValueKind.String => new RegistryDataSnapshot { DataKind = RegistryDataKind.String, StringValue = raw.ToString() },
            RegistryValueKind.ExpandString => new RegistryDataSnapshot { DataKind = RegistryDataKind.ExpandString, StringValue = raw.ToString() },
            RegistryValueKind.MultiString => new RegistryDataSnapshot { DataKind = RegistryDataKind.MultiString, MultiStringValue = raw is string[] values ? values.ToList() : [] },
            RegistryValueKind.DWord => new RegistryDataSnapshot { DataKind = RegistryDataKind.DWord, NumericValue = Convert.ToInt64(raw) },
            RegistryValueKind.QWord => new RegistryDataSnapshot { DataKind = RegistryDataKind.QWord, NumericValue = Convert.ToInt64(raw) },
            RegistryValueKind.Binary => new RegistryDataSnapshot { DataKind = RegistryDataKind.Binary, BinaryValue = raw as byte[] ?? [] },
            _ => new RegistryDataSnapshot { DataKind = RegistryDataKind.None }
        };
    }
}
