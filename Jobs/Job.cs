using System;
using System.Collections.Generic;
using System.Text;

namespace OnePieceCardScanner.Jobs;

public sealed class Job
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public JobType Type { get; set; }

    public JobStatus Status { get; set; } = JobStatus.WAITING;

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public string Description { get; set; } = string.Empty;
}
