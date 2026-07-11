namespace DotCarbon.Plugins.SingleInstance;

public record SingleInstanceStatus(bool IsPrimary, string MutexName, string[] Args);
