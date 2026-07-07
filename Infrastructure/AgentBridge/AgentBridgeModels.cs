using System;

namespace Syncro.Desktop.Infrastructure.AgentBridge
{
    public class AgentSession
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string AgentName { get; set; } = "";
        public string ProjectId { get; set; } = "";
        public string Status { get; set; } = "Active";
        public DateTime StartedAt { get; set; } = DateTime.UtcNow;
        public DateTime? CompletedAt { get; set; }
        public string CurrentTask { get; set; } = "";
    }

    public class AgentMessage
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid SessionId { get; set; }
        public string Type { get; set; } = "Status"; // Status, Progress, Error, Warning, Result, Heartbeat
        public string Message { get; set; } = "";
        public string Severity { get; set; } = "Info";
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public class AgentAction
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid SessionId { get; set; }
        public string ActionType { get; set; } = ""; // CloneRepo, ScanAst, GenerateController, PatchCode
        public string Input { get; set; } = "";
        public string Output { get; set; } = "";
        public bool Success { get; set; }
    }

    public class AgentFile
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid SessionId { get; set; }
        public string FilePath { get; set; } = "";
        public string FileType { get; set; } = "";
        public string ChangeType { get; set; } = ""; // Created, Modified, Deleted
        public string Hash { get; set; } = "";
    }

    public class AgentError
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid SessionId { get; set; }
        public string ErrorType { get; set; } = "";
        public string Message { get; set; } = "";
        public string StackTrace { get; set; } = "";
        public string RootCause { get; set; } = "";
        public string Resolution { get; set; } = "";
        public bool Resolved { get; set; }
    }

    public class HindsightRecord
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Problem { get; set; } = "";
        public string RootCause { get; set; } = "";
        public string Solution { get; set; } = "";
        public bool Success { get; set; }
        public float Confidence { get; set; } = 1.0f;
        public float[] Vector { get; set; } = Array.Empty<float>();
    }

    public class AgentKnowledge
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Topic { get; set; } = "";
        public string Content { get; set; } = "";
        public string SourceType { get; set; } = ""; // AST, Swagger, DTO, Architecture, Error, Memory
        public float[] Vector { get; set; } = Array.Empty<float>();
    }

    public class LlmActionRequest
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Prompt { get; set; } = "";
        public string ContextSource { get; set; } = "";
        public string Status { get; set; } = "Pending";
        public string Result { get; set; } = "";
    }

    public class AgentPacket
    {
        public string PacketType { get; set; } = ""; // Status, Progress, Action, Error, Result, Memory, LlmRequest, LlmResponse, FileChange
        public string AgentName { get; set; } = "";
        public string SessionId { get; set; } = "";
        public object? Payload { get; set; }
    }
}
