namespace TaskFlow.Api.Models;

// Tracks where a JobApplication's two sibling tasks (resume + cover letter) stand overall.
public enum ApplicationState
{
    Building,
    ReviewReady,
    Approved
}
