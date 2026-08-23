namespace OCR.Business.Models;

/// <summary>Bọc kết quả thành công/lỗi cho lỗi nghiệp vụ (không ném exception lung tung).</summary>
public sealed class Result
{
    public bool IsSuccess { get; }
    public string? Error { get; }

    private Result(bool ok, string? error)
    {
        IsSuccess = ok;
        Error = error;
    }

    public static Result Success() => new(true, null);
    public static Result Fail(string error) => new(false, error);
}

/// <summary>Như <see cref="Result"/> nhưng kèm giá trị trả về khi thành công.</summary>
public sealed class Result<T>
{
    public bool IsSuccess { get; }
    public T? Value { get; }
    public string? Error { get; }

    private Result(bool ok, T? value, string? error)
    {
        IsSuccess = ok;
        Value = value;
        Error = error;
    }

    public static Result<T> Success(T value) => new(true, value, null);
    public static Result<T> Fail(string error) => new(false, default, error);
}
