using System.Windows;
using System.Windows.Controls;
using CsharpLearning.Core.Model;

namespace CsharpLearning.App.View;

/// <summary>
/// Выбирает шаблон отрисовки для блока урока: заголовок, абзац, код, список, таблица.
/// </summary>
public sealed class BlockTemplateSelector : DataTemplateSelector
{
    public DataTemplate? Heading { get; set; }
    public DataTemplate? Paragraph { get; set; }
    public DataTemplate? Code { get; set; }
    public DataTemplate? List { get; set; }
    public DataTemplate? Table { get; set; }

    public override DataTemplate? SelectTemplate(object item, DependencyObject container)
    {
        if (item is not BlockVm block) return base.SelectTemplate(item, container);

        return block.Kind switch
        {
            BlockKind.Heading => Heading,
            BlockKind.Code => Code,
            BlockKind.List => List,
            BlockKind.Table => Table,
            _ => Paragraph,
        };
    }
}
