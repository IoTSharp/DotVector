namespace DotVector.Exceptions;

/// <summary>
/// DotVector 数据库操作的基础异常类型。
/// 所有 DotVector 特定异常均继承此类。
/// </summary>
public class DotVectorException : Exception
{
    /// <summary>
    /// 初始化 <see cref="DotVectorException"/> 的新实例。
    /// </summary>
    public DotVectorException()
    {
    }

    /// <summary>
    /// 使用指定的错误消息初始化 <see cref="DotVectorException"/> 的新实例。
    /// </summary>
    /// <param name="message">描述错误的消息。</param>
    public DotVectorException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// 使用指定的错误消息和内部异常初始化 <see cref="DotVectorException"/> 的新实例。
    /// </summary>
    /// <param name="message">描述错误的消息。</param>
    /// <param name="innerException">导致当前异常的异常。</param>
    public DotVectorException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
