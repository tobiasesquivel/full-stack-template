namespace TemplateApp.Infrastructure.Observability;

public interface ITemplateMetrics
{
    void AuthRegistered(string outcome);

    void AuthLoggedIn(string outcome);

    void TaskCreated();

    void TaskUpdated(string outcome);
}
