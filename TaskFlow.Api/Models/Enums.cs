namespace TaskFlow.Api.Models;

// Naming convention: a domain enum must never reuse a name from the .NET base class
// library. The old "TaskStatus" collided with System.Threading.Tasks.TaskStatus, so it
// is named WorkflowStatus. TaskPriority has no BCL clash and keeps its name.
public enum WorkflowStatus
{
    Todo,
    InProgress,
    Review,
    Done
}

public enum TaskPriority
{
    Low,
    Medium,
    High
}