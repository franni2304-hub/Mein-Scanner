using System;
using System.Collections.Generic;
using System.Text;

namespace OnePieceCardScanner.Jobs;

public enum JobStatus
{
    WAITING,
    RUNNING,
    COMPLETED,
    FAILED
}