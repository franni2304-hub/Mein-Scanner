using System;
using System.Collections.Generic;
using System.Text;

namespace OnePieceCardScanner.Jobs;

public enum JobType
{
    IMPORT,
    OCR,
    IMAGE_MATCH,
    STORAGE,
    DATABASE_SAVE,
    EBAY_UPLOAD,
    BACKUP,
    UPDATE_DATABASE
}