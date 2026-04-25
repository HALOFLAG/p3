// ModuleLoadResult — ModuleLoader 的結果型別（Success/Failure 多型）。
// ValidationError：檔案路徑、JSON pointer、訊息，供 ModuleErrorWindow 顯示。
using CardNarrative.Core.Models;

namespace CardNarrative.Core.Services;

public sealed record ValidationError(
    string FilePath,
    string JsonPointer,
    string Message,
    string? ExpectedType = null
)
{
    public override string ToString() =>
        ExpectedType is null
            ? $"{FilePath}#{JsonPointer}: {Message}"
            : $"{FilePath}#{JsonPointer}: {Message} (expected: {ExpectedType})";
}

public abstract record ModuleLoadResult
{
    public sealed record Success(Module Module) : ModuleLoadResult;
    public sealed record Failure(IReadOnlyList<ValidationError> Errors) : ModuleLoadResult;

    public bool IsSuccess => this is Success;
    public Module? ModuleOrNull => (this as Success)?.Module;
    public IReadOnlyList<ValidationError> ErrorsOrEmpty =>
        (this as Failure)?.Errors ?? Array.Empty<ValidationError>();
}
