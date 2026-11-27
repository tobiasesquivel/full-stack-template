namespace Dune.Analyzers.Tests;

internal static class ExemptionFixture
{
    public const string Path = "backend/Shared/Architecture/DuneExemptionAttribute.cs";

    public const string Source = """
        namespace TemplateApp.Shared.Architecture
        {
            public enum DuneCheck
            {
                IdempotentPost,
                ReadSliceLoadsEntity,
                MediatorEndpoint,
                TransactionOwner,
            }

            [System.AttributeUsage(System.AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
            public sealed class DuneExemptionAttribute : System.Attribute
            {
                public DuneExemptionAttribute(DuneCheck check, string reason)
                {
                    Check = check;
                    Reason = reason;
                }

                public DuneCheck Check { get; }

                public string Reason { get; }
            }
        }
        """;
}
