class IntelliFillError(Exception):
    """Base exception for user-recoverable application failures."""


class OCRUnavailableError(IntelliFillError):
    """Raised when Tesseract is not installed or cannot be executed."""


class UnsupportedDocumentError(IntelliFillError):
    """Raised when a selected document type cannot be parsed."""
