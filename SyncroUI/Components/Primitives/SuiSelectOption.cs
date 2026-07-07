namespace Syncro.Desktop.SyncroUI.Components.Primitives;

public class SuiSelectOption
{
    public string Value { get; set; }
    public string Text { get; set; }

    public SuiSelectOption(string value, string text)
    {
        Value = value;
        Text = text;
    }
}
