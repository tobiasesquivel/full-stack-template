namespace TemplateApp.Shared.Architecture;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class DuneExemptionAttribute : Attribute
{
    public DuneExemptionAttribute(DuneCheck check, string reason)
    {
        Check = check;
        Reason = reason;
    }

    public DuneCheck Check { get; }

    public string Reason { get; }
}
