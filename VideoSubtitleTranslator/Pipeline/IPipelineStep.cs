namespace VideoSubtitleTranslator.Pipeline;

/// <summary>
/// 业务流程中的“步骤”接口。
/// 所有实现类必须设计为<strong>无状态</strong>：不持有可变字段，只通过上下文字典读写数据。
/// </summary>
public interface IPipelineStep
{
    /// <summary>
    /// 步骤名称，用于业务含义标识（例如 "Download"、"ExtractSubtitle"、"Translate" 等）。
    /// </summary>
    string Step { get; }

    /// <summary>
    /// 执行一步业务逻辑。
    /// <para>入参和返回值都是字符串到字符串的映射，一般用于保存各类文件路径或小型配置值。</para>
    /// <para>实现类应当视 <paramref name="context"/> 为缓冲区，对其进行就地更新或返回新的字典，
    /// 但<strong>不要在实例内部缓存任何状态</strong>，以便该实现可以被视为纯函数式步骤。</para>
    /// </summary>
    /// <param name="context">当前的上下文字典，键通常为逻辑名，值为路径或简单配置。</param>
    /// <returns>更新后的上下文字典（可以是同一个实例，也可以是新的字典）。</returns>
    Dictionary<string, string> Execute(Dictionary<string, string> context);
}

