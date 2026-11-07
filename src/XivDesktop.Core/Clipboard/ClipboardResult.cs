namespace XivDesktop.Core.Clipboard;

public sealed record ClipboardResult(long Request, string Operation, bool Pending, bool Available,
    string Text, bool SensitivityKnown, bool Sensitive)
{
    public bool CanSynchronize => Operation == "read" && !Pending && Available && SensitivityKnown && !Sensitive;
    public bool WriteAcknowledged => Operation == "write" && !Pending && Available;
}
