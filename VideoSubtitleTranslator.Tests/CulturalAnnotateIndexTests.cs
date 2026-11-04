using VideoSubtitleTranslator.Pipeline;

namespace VideoSubtitleTranslator.Tests;

/// <summary>
/// 注释模型按"每批 1..N"编号，写盘前必须换算成全片序号；
/// 否则只有前 10 句能匹配上，后面的注释永远不会显示。
/// </summary>
public sealed class CulturalAnnotateIndexTests
{
    private static DeepSeekCulturalAnnotateStep.AnnotationItem Item(int index, string text = "x") =>
        new() { Index = index, NeedsAnnotation = true, Annotation = text };

    [Fact]
    public void Batch_indices_are_shifted_to_global_sentence_numbers()
    {
        var target = new SortedDictionary<int, DeepSeekCulturalAnnotateStep.AnnotationItem>();

        DeepSeekCulturalAnnotateStep.AddBatchAnnotations(target, [Item(1), Item(3)], batchStart: 20, sentenceCount: 63);

        Assert.Equal([21, 23], target.Keys.ToArray());
        Assert.Equal("x", target[21].Annotation);
    }

    [Fact]
    public void Indices_outside_the_subtitle_range_are_dropped()
    {
        var target = new SortedDictionary<int, DeepSeekCulturalAnnotateStep.AnnotationItem>();

        // 最后一批只有 3 句，模型却多报了第 8 条。
        DeepSeekCulturalAnnotateStep.AddBatchAnnotations(target, [Item(1), Item(8)], batchStart: 60, sentenceCount: 63);

        Assert.Equal([61], target.Keys.ToArray());
    }

    [Fact]
    public void Later_entries_in_the_same_batch_win()
    {
        var target = new SortedDictionary<int, DeepSeekCulturalAnnotateStep.AnnotationItem>();

        DeepSeekCulturalAnnotateStep.AddBatchAnnotations(target, [Item(2, "first"), Item(2, "second")], batchStart: 0, sentenceCount: 10);

        Assert.Single(target);
        Assert.Equal("second", target[2].Annotation);
    }
}
